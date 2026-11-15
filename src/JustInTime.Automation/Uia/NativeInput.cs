using System.ComponentModel;
using System.Runtime.InteropServices;

namespace JustInTime.Automation.Uia;

/// <summary>Ввод символов через SendInput (официальный Win32 API) как события клавиатуры Unicode.</summary>
internal static class NativeInput
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventFUnicode = 0x0004;
    private const uint KeyEventFKeyUp = 0x0002;

    public static void TypeUnicode(string text)
    {
        var inputs = new List<Input>(text.Length * 2);

        foreach (var unit in text)
        {
            inputs.Add(Key(unit, keyUp: false));
            inputs.Add(Key(unit, keyUp: true));
        }

        if (inputs.Count == 0)
        {
            return;
        }

        var array = inputs.ToArray();
        var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<Input>());

        if (sent != array.Length)
        {
            // Часть событий могла дойти: результат неизвестен, вызывающий проверит поле и вернёт честный итог.
            throw new UiAutomationException(
                $"Windows приняла {sent} из {array.Length} событий ввода",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private static Input Key(char unit, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = 0,
                ScanCode = unit,
                Flags = KeyEventFUnicode | (keyUp ? KeyEventFKeyUp : 0),
                Time = 0,
                ExtraInfo = IntPtr.Zero
            }
        }
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    // Объединение должно быть размером с самую большую структуру (MOUSEINPUT), иначе размер INPUT неверный.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
