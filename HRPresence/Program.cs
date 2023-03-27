using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Tomlyn;
using Tomlyn.Model;
using Timer = System.Timers.Timer;

namespace HRPresence
{
    internal class Config : ITomlMetadataProvider
    {
        public float TimeOutInterval { get; set; } = 3f;
        public float RestartDelay { get; set; } = 3f;
        public int OSCPort { get; set; } = 9000;
        public TomlPropertiesMetadata PropertiesMetadata { get; set; }
    }

    internal class Program
    {
        private static HeartRateService heartrate;
        private static HeartRateReading reading;
        private static OscService osc;

        private static DateTime lastUpdate = DateTime.MinValue;

        public static bool isHRConnected;
        private static bool isHeartBeat;
        private static int currentHR;
        private static int rrInterval;
        private static string programDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

        private static void Main()
        {
            var config = new Config();
            var configLocation = Path.Combine(programDir, "config.toml");
            var hrTxtLocation = Path.Combine(programDir, "HR.txt");
            if (File.Exists(configLocation))
            {
                config = Toml.ToModel<Config>(File.OpenText(configLocation).ReadToEnd());
            }
            else
            {
                File.WriteAllText(configLocation, Toml.FromModel(config));
            }

            Console.CursorVisible = false;
            Console.WindowHeight = 4;
            Console.WindowWidth = 32;

            osc = new OscService();
            osc.Initialize(System.Net.IPAddress.Loopback, config.OSCPort);

            heartrate = new HeartRateService();
            heartrate.HeartRateUpdated += heart =>
            {
                reading = heart;
                currentHR = heart.BeatsPerMinute;
                rrInterval = heart.RRIntervals != null && heart.RRIntervals.Length > 0 && heart.RRIntervals[0] > 0 ? heart.RRIntervals[0] : 0;

                Console.Write($"{DateTime.Now}: {currentHR} BPM\n");

                lastUpdate = DateTime.Now;
                File.WriteAllText(hrTxtLocation, $"{currentHR}");

                osc.Update(currentHR, rrInterval);
                if (!isHeartBeat)
                    HeartBeat();
            };

            while (true)
            {
                if (DateTime.Now - lastUpdate > TimeSpan.FromSeconds(config.TimeOutInterval + 2))
                {
                    isHRConnected = false;
                    osc.Clear();
                }

                if (DateTime.Now - lastUpdate > TimeSpan.FromSeconds(config.TimeOutInterval))
                {
                    Console.Clear();
                    Console.Write("Connecting...\n");
                    while (true)
                    {
                        try
                        {
                            heartrate.InitiateDefault();
                            isHRConnected = true;
                            Console.Clear();
                            break;
                        }
                        catch (Exception e)
                        {
                            isHRConnected = false;
                            osc.Clear();
                            Console.Clear();
                            Console.Write($"Failure while initiating heartrate service, retrying in {config.RestartDelay} seconds:\n");
                            Debug.WriteLine(e);
                            Thread.Sleep((int)(config.RestartDelay * 1000));
                        }
                    }
                }

                Thread.Sleep(2000);
            }
        }

        private static void HeartBeat()
        {
            if (currentHR == 0 || !isHRConnected)
            {
                isHeartBeat = false;
                return;
            }

            isHeartBeat = true;

            // Use the class-level variable for the RR interval (in ms) as the wait time between heartbeats
            int waitTime = rrInterval;
            // If the RR interval is 0, use the old method of calculating the wait time
            if (rrInterval == 0)
                waitTime = defaultWaitTime(currentHR);

            new ExecuteInTime(waitTime, (eit) =>
            {
                osc.SendBeat();
                // Recursively call HeartBeat() to maintain the heartbeat loop
                HeartBeat();
            });
        }

        private static int defaultWaitTime(int currentHR)
        {
            float waitTime = 1 / ((currentHR - 0.1f) / 60);
            return (int)(waitTime * 1000);
        }

        public class ExecuteInTime
        {
            private readonly Timer timer;
            public ExecuteInTime(int ms, Action<ExecuteInTime> callback)
            {
                timer = new Timer(ms);
                timer.AutoReset = false;
                timer.Elapsed += (sender, args) =>
                {
                    callback.Invoke(this);
                    timer.Stop();
                    timer.Close();
                };
                timer.Start();
            }
        }
    }
}