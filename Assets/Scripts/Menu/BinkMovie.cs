using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

/// <summary>
/// 运行时 Bink1 ('BIKi') 解码播放器。
/// 64 位 Unity 进程无法加载 32 位 binkw32.dll，因此由 32 位子进程解码、
/// 通过 stdout 把 BGRA 帧传回，主线程直接写入 Texture2D。没有转码、没有序列帧。
/// </summary>
public sealed class BinkMovie : IDisposable
{
    private const uint Magic = 0x324B4642; // 'BFK2'
    private const int HeaderBytes = 28;    // magic + w + h + frames + rateNum + rateDen + audioTracks

    private Process _process;
    private Thread _reader;
    private volatile bool _running;

    private readonly object _sync = new object();
    private readonly Queue<byte[]> _ready = new Queue<byte[]>();
    private readonly Queue<byte[]> _free = new Queue<byte[]>();

    private volatile bool _finished;
    private volatile bool _failed;
    private string _error = string.Empty;

    private int _width;
    private int _height;
    private int _frameCount;
    private int _rateNum = 15;
    private int _rateDen = 1;
    private int _frameBytes;

    private Texture2D _texture;
    private volatile int _lastFrame = -1;
    private volatile int _decodedFrames;

    private int _audioTracks;
    private int _volumePercent = 100;
    private bool _volumeDirty;

    public int Width { get { return _width; } }
    public int Height { get { return _height; } }
    public int FrameCount { get { return _frameCount; } }
    public float Fps { get { return _rateDen > 0 ? _rateNum / (float)_rateDen : 0f; } }
    public Texture2D Texture { get { return _texture; } }
    public bool Finished { get { return _finished; } }
    public bool Failed { get { return _failed; } }
    public string Error { get { return _error; } }
    public int DecodedFrames { get { return _decodedFrames; } }
    public int LastFrameNumber { get { return _lastFrame; } }
    public bool IsPlaying { get { return _running && !_finished && !_failed; } }

    /// <summary>bik 内嵌音轨数量（0 表示无音轨，由 Bink 自己的 DirectSound 后端直接出声）。</summary>
    public int AudioTracks { get { return _audioTracks; } }
    public bool HasAudio { get { return _audioTracks > 0; } }

    /// <summary>启动解码子进程。loop=true 时无限循环（Bink 自身按 15Hz 节拍输出，音频由 Bink 的 DirectSound 后端播放）。</summary>
    public static BinkMovie Start(string exePath, string dllPath, string bikPath, bool loop, int width, int height)
    {
        var movie = new BinkMovie();
        movie.Launch(exePath, dllPath, bikPath, loop, width, height);
        return movie;
    }

    /// <summary>影片内嵌音轨音量（0-100）。由 Unity 侧通过 stdin 的 "V n" 命令下发给解码进程。</summary>
    public void SetVolume(int percent)
    {
        percent = percent < 0 ? 0 : percent > 100 ? 100 : percent;
        if (_volumePercent == percent) return;
        _volumePercent = percent;
        _volumeDirty = true;
    }

    private void FlushVolume()
    {
        if (!_volumeDirty || _process == null) return;
        _volumeDirty = false;
        try
        {
            var stdin = _process.StandardInput;
            if (stdin != null)
            {
                stdin.WriteLine("V " + _volumePercent);
                stdin.Flush();
            }
        }
        catch
        {
            // 进程已退出，忽略
        }
    }

    private void Launch(string exePath, string dllPath, string bikPath, bool loop, int expectedWidth, int expectedHeight)
    {
        if (!File.Exists(exePath)) { _failed = true; _error = "缺少解码器 " + exePath; return; }
        if (!File.Exists(dllPath)) { _failed = true; _error = "缺少 BINKW32.DLL " + dllPath; return; }
        if (!File.Exists(bikPath)) { _failed = true; _error = "缺少影片 " + bikPath; return; }

        var info = new ProcessStartInfo();
        info.FileName = exePath;
        info.Arguments = "\"" + dllPath + "\" \"" + bikPath + "\" " + (loop ? "1" : "0");
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.RedirectStandardInput = true;
        info.WorkingDirectory = Path.GetDirectoryName(dllPath) ?? Application.streamingAssetsPath;

        _process = new Process();
        _process.StartInfo = info;
        _process.Start();

        _width = expectedWidth;
        _height = expectedHeight;
        _frameBytes = expectedWidth * expectedHeight * 4;

        _running = true;
        _reader = new Thread(ReadLoop);
        _reader.IsBackground = true;
        _reader.Name = "BinkReader";
        _reader.Start();
    }

    private void ReadLoop()
    {
        try
        {
            var stdout = _process.StandardOutput.BaseStream;
            var header = new byte[HeaderBytes];
            if (!ReadExact(stdout, header, 0, HeaderBytes))
            {
                Fail("读取 Bink 流头失败");
                return;
            }

            var magic = BitConverter.ToUInt32(header, 0);
            if (magic != Magic)
            {
                Fail("Bink 流头魔数不符：" + magic.ToString("X8"));
                return;
            }

            _width = (int)BitConverter.ToUInt32(header, 4);
            _height = (int)BitConverter.ToUInt32(header, 8);
            _frameCount = (int)BitConverter.ToUInt32(header, 12);
            _rateNum = (int)BitConverter.ToUInt32(header, 16);
            _rateDen = (int)BitConverter.ToUInt32(header, 20);
            _audioTracks = (int)BitConverter.ToUInt32(header, 24);
            _frameBytes = _width * _height * 4;

            UnityEngine.Debug.Log(string.Format(
                "[Bink] 影片已打开 {0}x{1} 帧数={2} {3}fps 音轨={4}", _width, _height, _frameCount, Fps, _audioTracks));

            FlushVolume();

            var head = new byte[8];
            while (_running)
            {
                if (!ReadExact(stdout, head, 0, 8))
                {
                    _finished = true;
                    UnityEngine.Debug.Log("[Bink] 影片播放结束（流关闭）");
                    return;
                }

                var frameNumber = (int)BitConverter.ToUInt32(head, 0);
                var length = (int)BitConverter.ToUInt32(head, 4);
                if (length <= 0 || length > _frameBytes + 64)
                {
                    Fail("帧长度异常：" + length);
                    return;
                }

                var buffer = RentBuffer(length);
                if (!ReadExact(stdout, buffer, 0, length))
                {
                    _finished = true;
                    return;
                }

                // binkw32 输出 BGRA 且第 0 行在顶部：就地换 B/R，并把行序翻成 Unity 的左下原点
                for (var i = 0; i + 3 < length; i += 4)
                {
                    var b = buffer[i];
                    buffer[i] = buffer[i + 2];
                    buffer[i + 2] = b;
                }
                FlipRows(buffer, _width, _height, length);

                _lastFrame = frameNumber;
                _decodedFrames++;

                lock (_sync)
                {
                    while (_ready.Count >= 2)
                    {
                        _free.Enqueue(_ready.Dequeue());
                    }
                    _ready.Enqueue(buffer);
                }
            }
        }
        catch (Exception e)
        {
            Fail("Bink 读取线程异常：" + e.Message);
        }
    }

    private byte[] RentBuffer(int length)
    {
        lock (_sync)
        {
            if (_free.Count > 0)
            {
                var reused = _free.Dequeue();
                if (reused.Length == length) return reused;
            }
        }
        return new byte[length];
    }

    private void Fail(string message)
    {
        var stderr = string.Empty;
        try
        {
            if (_process != null && !_process.HasExited)
            {
                stderr = _process.StandardError.ReadToEnd();
            }
        }
        catch
        {
            // ignored
        }

        _error = stderr.Length > 0 ? message + " / " + stderr.Trim() : message;
        _failed = true;
        _finished = true;
        UnityEngine.Debug.LogError("[Bink] " + _error);
    }

    private static void FlipRows(byte[] rgba, int width, int height, int length)
    {
        var stride = width * 4;
        if (stride <= 0 || height <= 1 || length < stride * height) return;
        var tmp = new byte[stride];
        for (var y = 0; y < height / 2; y++)
        {
            var top = y * stride;
            var bottom = (height - 1 - y) * stride;
            Buffer.BlockCopy(rgba, top, tmp, 0, stride);
            Buffer.BlockCopy(rgba, bottom, rgba, top, stride);
            Buffer.BlockCopy(tmp, 0, rgba, bottom, stride);
        }
    }

    private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, offset + total, count - total);
            if (read <= 0) return false;
            total += read;
        }
        return true;
    }

    /// <summary>主线程每帧调用；有新帧则刷新纹理并返回 true。</summary>
    public bool TryUpdate()
    {
        FlushVolume();
        if (_width <= 0 || _height <= 0) return false;

        byte[] newest = null;
        lock (_sync)
        {
            while (_ready.Count > 0)
            {
                newest = _ready.Dequeue();
            }
        }

        if (newest == null) return false;

        if (_texture == null)
        {
            _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
            _texture.wrapMode = TextureWrapMode.Clamp;
            _texture.filterMode = FilterMode.Bilinear;
            _texture.name = "BinkFrame";
        }

        if (newest.Length >= _frameBytes)
        {
            _texture.LoadRawTextureData(newest);
            _texture.Apply(false);
        }

        lock (_sync)
        {
            _free.Enqueue(newest);
        }
        return true;
    }

    public void Stop()
    {
        _running = false;
        try
        {
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.StandardInput.Close();
                }
                catch
                {
                    // ignored
                }
                _process.Kill();
            }
        }
        catch
        {
            // ignored
        }
        _reader = null;
    }

    public void Dispose()
    {
        Stop();
        try
        {
            if (_process != null)
            {
                _process.Dispose();
                _process = null;
            }
        }
        catch
        {
            // ignored
        }

        lock (_sync)
        {
            _ready.Clear();
            _free.Clear();
        }

        if (_texture != null)
        {
            UnityEngine.Object.Destroy(_texture);
            _texture = null;
        }
    }
}
