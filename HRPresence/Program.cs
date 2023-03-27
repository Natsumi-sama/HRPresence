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

                Console.Write($"{DateTime.Now}: {currentHR} BPM\n");

                lastUpdate = DateTime.Now;
                File.WriteAllText(hrTxtLocation, $"{currentHR}");

                osc.Update(currentHR);
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

            // https://github.com/200Tigersbloxed/HRtoVRChat_OSC/blob/c73ae8224dfed35e743c0c436393607d5eb191e8/HRtoVRChat_OSC/Program.cs#L503
            // When lowering the HR significantly, this will cause issues with the beat bool
            // Dubbed the "Breathing Exercise" bug
            // There's a 'temp' fix for it right now, but I'm not sure how it'll hold up
            float waitTime = default(float);
            try { waitTime = 1 / ((currentHR - 0.1f) / 60); } catch (DivideByZeroException) { /*Just a Divide by Zero Exception*/ }
            var executeInTime = new ExecuteInTime((int)(waitTime * 1000), eit =>
            {
                osc.SendBeat();
                HeartBeat();
            });
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