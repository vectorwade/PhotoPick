using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PhotoPick.Desktop.Services;

public enum GamepadAction
{
    PreviousPhoto,
    NextPhoto,
    Pick,
    Reject,
    ToggleFocusPeaking,
    NavigateUp,
    NavigateDown,
    NavigateLeft,
    NavigateRight
}

public sealed class GamepadService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    private const int ERROR_SUCCESS = 0;
    private const int ERROR_DEVICE_NOT_CONNECTED = 1167;

    // XInput Button Constants
    private const ushort DPAD_UP = 0x0001;
    private const ushort DPAD_DOWN = 0x0002;
    private const ushort DPAD_LEFT = 0x0004;
    private const ushort DPAD_RIGHT = 0x0008;
    private const ushort LEFT_SHOULDER = 0x0100;  // LB
    private const ushort RIGHT_SHOULDER = 0x0200; // RB
    private const ushort BUTTON_A = 0x1000;       // Face Down (Pick)
    private const ushort BUTTON_B = 0x2000;       // Face Right (Reject)
    private const ushort BUTTON_X = 0x4000;       // Face Left (Reject)
    private const ushort BUTTON_Y = 0x8000;       // Face Up (Focus Peaking)
    private const byte TRIGGER_THRESHOLD = 50;

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState", CallingConvention = CallingConvention.StdCall)]
    private static extern int XInputGetState_1_4(int dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState", CallingConvention = CallingConvention.StdCall)]
    private static extern int XInputGetState_9_1_0(int dwUserIndex, out XINPUT_STATE pState);

    private static bool _useFallbackDll;

    private static int SafeXInputGetState(int dwUserIndex, out XINPUT_STATE pState)
    {
        if (!_useFallbackDll)
        {
            try
            {
                return XInputGetState_1_4(dwUserIndex, out pState);
            }
            catch (DllNotFoundException)
            {
                _useFallbackDll = true;
            }
        }

        try
        {
            return XInputGetState_9_1_0(dwUserIndex, out pState);
        }
        catch
        {
            pState = default;
            return ERROR_DEVICE_NOT_CONNECTED;
        }
    }

    public event Action<GamepadAction>? ActionTriggered;
    public event Action<bool>? ConnectionChanged;

    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private bool _isConnected;
    private ushort _lastButtons;
    private bool _lastLtPressed;
    private bool _lastRtPressed;

    public bool IsConnected => _isConnected;

    public void Start()
    {
        if (_pollTask != null) return;

        _cts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _pollTask?.Wait(200);
        }
        catch { /* ignore cancellation */ }
        _pollTask = null;
        _cts?.Dispose();
        _cts = null;
    }

    private async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            int result = SafeXInputGetState(0, out XINPUT_STATE state);
            bool currentlyConnected = (result == ERROR_SUCCESS);

            if (currentlyConnected != _isConnected)
            {
                _isConnected = currentlyConnected;
                DispatchConnectionChanged(_isConnected);
            }

            if (currentlyConnected)
            {
                ProcessInput(state.Gamepad);
            }

            try
            {
                await Task.Delay(25, ct); // ~40Hz polling loop
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ProcessInput(XINPUT_GAMEPAD pad)
    {
        ushort currentButtons = pad.wButtons;
        bool currentLt = pad.bLeftTrigger > TRIGGER_THRESHOLD;
        bool currentRt = pad.bRightTrigger > TRIGGER_THRESHOLD;

        // Debounce / Rising edge detection (pressed now, but was not pressed last frame)
        bool JustPressed(ushort mask) => (currentButtons & mask) != 0 && (_lastButtons & mask) == 0;

        // Triggers and Bumpers -> Prev / Next photo
        if ((currentLt && !_lastLtPressed) || JustPressed(LEFT_SHOULDER))
        {
            DispatchAction(GamepadAction.PreviousPhoto);
        }
        else if ((currentRt && !_lastRtPressed) || JustPressed(RIGHT_SHOULDER))
        {
            DispatchAction(GamepadAction.NextPhoto);
        }

        // Face button A -> Pick (Green)
        if (JustPressed(BUTTON_A))
        {
            DispatchAction(GamepadAction.Pick);
        }

        // Face button B or X -> Reject (Red)
        if (JustPressed(BUTTON_B) || JustPressed(BUTTON_X))
        {
            DispatchAction(GamepadAction.Reject);
        }

        // Face button Y -> Focus Peaking
        if (JustPressed(BUTTON_Y))
        {
            DispatchAction(GamepadAction.ToggleFocusPeaking);
        }

        // D-Pad Grid navigation
        if (JustPressed(DPAD_LEFT))
        {
            DispatchAction(GamepadAction.NavigateLeft);
        }
        else if (JustPressed(DPAD_RIGHT))
        {
            DispatchAction(GamepadAction.NavigateRight);
        }
        else if (JustPressed(DPAD_UP))
        {
            DispatchAction(GamepadAction.NavigateUp);
        }
        else if (JustPressed(DPAD_DOWN))
        {
            DispatchAction(GamepadAction.NavigateDown);
        }

        _lastButtons = currentButtons;
        _lastLtPressed = currentLt;
        _lastRtPressed = currentRt;
    }

    private void DispatchAction(GamepadAction action)
    {
        var app = Application.Current;
        if (app != null && !app.Dispatcher.HasShutdownStarted)
        {
            app.Dispatcher.BeginInvoke(() =>
            {
                ActionTriggered?.Invoke(action);
            });
        }
    }

    private void DispatchConnectionChanged(bool connected)
    {
        var app = Application.Current;
        if (app != null && !app.Dispatcher.HasShutdownStarted)
        {
            app.Dispatcher.BeginInvoke(() =>
            {
                ConnectionChanged?.Invoke(connected);
            });
        }
    }

    public void Dispose()
    {
        Stop();
    }
}