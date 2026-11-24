using System.Runtime.InteropServices;
using JustInTime.Core.Localization;

namespace JustInTime.SystemControl;

/// <summary>
/// Громкость через Windows Core Audio (IMMDeviceEnumerator, IAudioEndpointVolume). Это официальный COM API Windows,
/// сторонние библиотеки не нужны. Меняется общая громкость устройства по умолчанию, как ползунок в системном микшере.
/// </summary>
public sealed class WindowsVolumeService : IVolumeService
{
    private const int ToleranceStep = 2;

    public Task<VolumeResult> SetAsync(int levelPercent, bool unmute, CancellationToken cancellationToken) =>
        Task.Run(() => Set(levelPercent, unmute, cancellationToken), cancellationToken);

    private static VolumeResult Set(int levelPercent, bool unmute, CancellationToken cancellationToken)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpoint = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

            try
            {
                enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x80070490) // E_NOTFOUND: устройства нет
            {
                return VolumeResult.Error(VolumeStatus.NoDevice, Loc.T("vol_no_device"));
            }

            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var activated);
            endpoint = (IAudioEndpointVolume)activated;

            cancellationToken.ThrowIfCancellationRequested();

            var eventContext = Guid.Empty;
            endpoint.SetMasterVolumeLevelScalar(levelPercent / 100f, ref eventContext);

            if (unmute)
            {
                endpoint.SetMute(false, ref eventContext);
            }

            endpoint.GetMasterVolumeLevelScalar(out var actualScalar);
            endpoint.GetMute(out var muted);

            var actual = (int)Math.Round(actualScalar * 100);
            var levelOk = Math.Abs(actual - levelPercent) <= ToleranceStep;
            var muteOk = !unmute || !muted;

            return levelOk && muteOk
                ? VolumeResult.Done()
                : VolumeResult.Error(
                    VolumeStatus.NotConfirmed,
                    Loc.T("vol_not_confirmed", actual, levelPercent, Loc.T(muted ? "vol_state_muted" : "vol_state_on")));
        }
        catch (COMException ex)
        {
            return VolumeResult.Error(VolumeStatus.Failed, Loc.T("vol_failed", ex.Message));
        }
        catch (InvalidCastException ex)
        {
            return VolumeResult.Error(VolumeStatus.Failed, Loc.T("vol_failed", ex.Message));
        }
        finally
        {
            Release(endpoint);
            Release(device);
            Release(enumerator);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    private const uint ClsCtxAll = 23; // CLSCTX_INPROC_SERVER | CLSCTX_INPROC_HANDLER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER

    private enum EDataFlow { Render = 0, Capture = 1, All = 2 }

    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);

        void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);

        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr properties);

        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);

        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);

        [PreserveSig] int GetChannelCount(out uint count);

        void SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

        void SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        void GetMasterVolumeLevel(out float levelDb);

        void GetMasterVolumeLevelScalar(out float level);

        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);

        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);

        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);

        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
