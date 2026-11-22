-- test_hooks.lua —— 生命周期事件测试脚本（外部可替换，StreamingAssets/Lua）
--
-- 事件由 C# 主循环在固定相位回调：
--   OnGameStart  游戏启动      OnGameEnd  游戏结束
--   OnMatchStart 每场对局开始  OnMatchEnd 每场对局结束
--   OnUpdate(dt) 对局进行中每帧
-- 钩子不存在时 C# 静默跳过；钩子抛错由 C# 侧打印异常与堆栈。
-- Lua 侧如需自保，可用 safe_wrap 包一层，通过 debug.traceback 输出出错位置。

local function log(event, extra)
    if extra ~= nil then
        print(string.format("[lua] 事件 %s: %s", event, tostring(extra)))
    else
        print("[lua] 事件 " .. event)
    end
end

-- 把一个处理函数包成带堆栈的安全版本：出错不抛出，直接打印 traceback。
local function safe_wrap(name, fn)
    return function(...)
        local ok, err = pcall(fn, ...)
        if not ok then
            print("[lua] " .. name .. " 出错: " .. tostring(err))
            print(debug.traceback())
        end
    end
end

function OnGameStart() log("OnGameStart 游戏启动") end
function OnGameEnd() log("OnGameEnd 游戏结束") end
function OnMatchStart() log("OnMatchStart 对局开始") end
function OnMatchEnd() log("OnMatchEnd 对局结束") end

local updateCount = 0
function OnUpdate(dt)
    updateCount = updateCount + 1
    if updateCount % 60 == 1 then
        print(string.format("[lua] OnUpdate dt=%.3fs 帧=%d", dt, updateCount))
    end
end

print("[lua] test_hooks.lua 加载完成")
