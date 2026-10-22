using NAudio.CoreAudioApi;

namespace GHelper.Helpers
{
    internal class Audio
    {
        static AudioEndpointVolume? endpointVolume; // keeps the COM callback alive

        public static void SubscribeMute(Action<bool> onMuteChange)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                endpointVolume = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).AudioEndpointVolume;
                bool muted = endpointVolume.Mute;
                onMuteChange(muted);
                endpointVolume.OnVolumeNotification += (data) => { if (data.Muted != muted) onMuteChange(muted = data.Muted); };
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Error subscribing to volume notifications: " + ex.Message);
            }
        }

        /// <summary>Master volume of the default playback device, 0-100 (-1 if unavailable).</summary>
        public static int GetVolume()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return (int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Volume read failed: " + ex.Message);
                return -1;
            }
        }

        /// <summary>
        /// Changes the master volume by delta percent, set directly (exact steps, no Windows volume flyout). Turning up unmutes.
        /// Returns the new volume (or -1).
        /// </summary>
        public static int AdjustVolume(int delta)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var volume = device.AudioEndpointVolume;
                int value = Math.Clamp((int)Math.Round(volume.MasterVolumeLevelScalar * 100) + delta, 0, 100);
                volume.MasterVolumeLevelScalar = value / 100f;
                if (delta > 0 && volume.Mute) volume.Mute = false;
                return value;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Volume change failed: " + ex.Message);
                return -1;
            }
        }

        public static bool ToggleMicMute()
        {
            using (var enumerator = new MMDeviceEnumerator())
            {
                var commDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                var consoleDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                var mmDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

                bool status = !commDevice.AudioEndpointVolume.Mute;
                
                commDevice.AudioEndpointVolume.Mute = status;
                consoleDevice.AudioEndpointVolume.Mute = status;
                mmDevice.AudioEndpointVolume.Mute = status;

                Logger.WriteLine(commDevice.ToString() + ":" + status);
                Logger.WriteLine(consoleDevice.ToString() + ":" + status);
                Logger.WriteLine(mmDevice.ToString() + ":" + status);

                return status;
            }
        }

        public static bool IsMicMuted()
        {
            try
            {
                using (var deviceEnumerator = new MMDeviceEnumerator())
                {
                    var commDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                    return commDevice.AudioEndpointVolume.Mute;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Error checking mic mute status: " + ex.Message);
                return false; // Assume not muted in case of error
            }
        }

        public static bool IsMuted()
        {
            try
            {
                using (var deviceEnumerator = new MMDeviceEnumerator())
                {
                    var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    return defaultDevice.AudioEndpointVolume.Mute;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Error checking mute status: " + ex.Message);
                return false; // Assume not muted in case of error
            }
        }
    }
}
