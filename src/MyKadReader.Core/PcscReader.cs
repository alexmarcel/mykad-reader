using System.Runtime.InteropServices;

namespace MyKadReader.Core;

public sealed record ReaderState(string Name, bool Present, uint Revision);
public interface ICardReader
{
    IReadOnlyList<ReaderState> Snapshot();
    Identity Read(string reader, CancellationToken cancellation);
}

public sealed class PcscReader : ICardReader
{
    public IReadOnlyList<ReaderState> Snapshot()
    {
        using var context = new Context();
        uint length = 0;
        var status = Native.SCardListReaders(context.Handle, null, null, ref length);
        if (status == 0x8010002E) return [];
        Check(status);
        if (length is 0 or > 65536) return [];
        var names = new char[length];
        Check(Native.SCardListReaders(context.Handle, null, names, ref length));
        var states = new string(names).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(name => new Native.State { Reader = name, Atr = new byte[36] }).ToArray();
        if (states.Length == 0) return [];
        Check(Native.SCardGetStatusChange(context.Handle, 0, states, (uint)states.Length));
        return states.Select(s => new ReaderState(s.Reader, (s.EventState & 0x20) != 0,
            s.EventState & 0xFFFF0020)).ToArray(); // Counter + presence, excluding busy/changed flags.
    }

    public Identity Read(string reader, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var channel = new Channel(reader);
        return new CardProtocol(channel, cancellation).Read();
    }

    internal static void Check(uint status)
    {
        if (status == 0) return;
        string message = status switch
        {
            0x8010001D or 0x8010001E => "Windows Smart Card service is unavailable. Check Windows services and reconnect the reader.",
            0x8010000C => "No card is inserted. Insert the MyKad with its chip facing the reader contacts.",
            0x80100017 or 0x80100069 => "The card or reader was removed. Reconnect and read again.",
            0x8010000B => "The reader is being used by another application. Close that application and retry.",
            0x80100009 or 0x8010002E => "No reader is available. Connect the reader and check its Windows driver.",
            0x80100068 => "The card was reset. Read it again.",
            _ => "Smart-card communication failed. Reconnect the reader and try again."
        };
        throw new UserVisibleException($"{message} (PC/SC {status:X8})");
    }

    private sealed class Context : IDisposable
    {
        public IntPtr Handle { get; }
        public Context() { Check(Native.SCardEstablishContext(0, IntPtr.Zero, IntPtr.Zero, out var handle)); Handle = handle; }
        public void Dispose() => Native.SCardReleaseContext(Handle);
    }

    private sealed class Channel : ICardChannel
    {
        private readonly Context context = new();
        private readonly IntPtr card;
        private readonly uint protocol;
        public Channel(string reader)
        {
            try { Check(Native.SCardConnect(context.Handle, reader, 1, 3, out card, out protocol)); }
            catch { context.Dispose(); throw; }
        }
        public byte[] Transmit(byte[] command)
        {
            var pci = new Native.Pci { Protocol = protocol, Length = 8 };
            byte[] buffer = new byte[4096];
            uint length = (uint)buffer.Length;
            Check(Native.SCardTransmit(card, ref pci, command, (uint)command.Length, IntPtr.Zero, buffer, ref length));
            if (length > buffer.Length) throw new InvalidDataException("Invalid smart-card response size.");
            return buffer[..(int)length];
        }
        public void Dispose() { Native.SCardDisconnect(card, 0); context.Dispose(); }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Pci { public uint Protocol; public uint Length; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct State
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string Reader;
            public IntPtr UserData;
            public uint CurrentState, EventState, AtrLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)] public byte[] Atr;
        }
        [DllImport("winscard.dll")] internal static extern uint SCardEstablishContext(uint scope, IntPtr a, IntPtr b, out IntPtr context);
        [DllImport("winscard.dll")] internal static extern uint SCardReleaseContext(IntPtr context);
        [DllImport("winscard.dll", CharSet = CharSet.Unicode, EntryPoint = "SCardListReadersW")]
        internal static extern uint SCardListReaders(IntPtr context, string? groups, [Out] char[]? readers, ref uint length);
        [DllImport("winscard.dll", CharSet = CharSet.Unicode, EntryPoint = "SCardGetStatusChangeW")]
        internal static extern uint SCardGetStatusChange(IntPtr context, uint timeout, [In, Out] State[] readers, uint count);
        [DllImport("winscard.dll", CharSet = CharSet.Unicode, EntryPoint = "SCardConnectW")]
        internal static extern uint SCardConnect(IntPtr context, string reader, uint share, uint protocols, out IntPtr card, out uint protocol);
        [DllImport("winscard.dll")] internal static extern uint SCardDisconnect(IntPtr card, uint disposition);
        [DllImport("winscard.dll")]
        internal static extern uint SCardTransmit(IntPtr card, ref Pci pci, byte[] send, uint sendLength, IntPtr receivePci, [Out] byte[] receive, ref uint receiveLength);
    }
}
