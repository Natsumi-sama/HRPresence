using System.Diagnostics;
using System.Net;
using OscQueryLibrary;
using Tomlyn;
using Tomlyn.Model;

namespace HR_GATT_OSC;

internal class Config : ITomlMetadataProvider
{
    public float TimeOutInterval { get; set; } = 3f;
    public float RestartDelay { get; set; } = 3f;
    public bool WriteToTxt { get; set; } = false;
    public bool QuestStandalone { get; set; } = false;
    public TomlPropertiesMetadata PropertiesMetadata { get; set; }
}

internal class Program
{
    private static HeartRateService heartrate;
    private static HeartRateReading reading;
    private static OscService oscSender;
    private static OscServer oscReceiver;

    private static DateTime lastUpdate = DateTime.MinValue;

    public static bool isHRConnected;
    private static bool isHeartBeat;
    private static int currentHR;
    private static int rrInterval;
    private static int maxBPM;
    private static DateTime maxBPMTime;
    private static int minBPM = int.MaxValue;
    private static DateTime minBPMTime;
    private static int connectionCount;

    private static readonly string programDir = AppDomain.CurrentDomain.BaseDirectory;

    private static void Main()
    {
        var config = new Config();
        var configLocation = Path.Combine(programDir, "config.toml");
        var hrTxtLocation = Path.Combine(programDir, "HR.txt");
        try
        {
            if (File.Exists(configLocation))
            {
                config = Toml.ToModel<Config>(File.OpenText(configLocation).ReadToEnd());
            }
            else
            {
                File.WriteAllText(configLocation, Toml.FromModel(config));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Invalid config.toml file, please fix or delete it and restart the program.");
            Console.Read();
            return;
        }

        Console.Title = "HRMonitor";
        // Console.CursorVisible = false;
        // Console.WindowHeight = 4;
        // Console.WindowWidth = 32;
        
        oscSender = new OscService();
        oscReceiver = new OscServer();
        
        var oscQueryServer = new OscQueryServer(
            "HRMonitor", // service name
            "127.0.0.1", // ip address for udp and http server
            () =>
            {
                oscSender.Initialize(IPAddress.Loopback, OscQueryServer.OscSendPort);
                oscReceiver.ListenForOscMessages(OscQueryServer.OscReceivePort);
            }, // optional callback on vrc discovery
            oscSender.UpdateAvailableParameters // parameter list callback on vrc discovery
        );

        if (config.QuestStandalone)
        {
            // listen for VRC on every network interface
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    continue;
            
                var ipAddress = ip.ToString();
                _ = new OscQueryServer(
                    "HRMonitor", // service name
                    ipAddress, // ip address for udp and http server
                    () =>
                    {
                        oscSender.Initialize(ip, OscQueryServer.OscSendPort);
                        oscReceiver.ListenForOscMessages(OscQueryServer.OscReceivePort);
                    }, // optional callback on vrc discovery
                    oscSender.UpdateAvailableParameters // parameter list callback on vrc discovery
                );
            }
        }
        
        heartrate = new HeartRateService();
        heartrate.HeartRateUpdated += heart =>
        {
            reading = heart;
            currentHR = heart.BeatsPerMinute;
            rrInterval = heart.RRIntervals != null && heart.RRIntervals.Length > 0 && heart.RRIntervals[0] > 0
                ? heart.RRIntervals[0]
                : 0;

            if (currentHR > maxBPM)
            {
                maxBPM = currentHR;
                maxBPMTime = DateTime.Now;
            }
            if (currentHR != 0 && currentHR < minBPM)
            {
                minBPM = currentHR;
                minBPMTime = DateTime.Now;
            }

            // Console.SetCursorPosition(0, 0);
            Console.Clear();
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}".PadRight(32));
            Console.WriteLine($"BPM: {currentHR}".PadRight(32));
            if (maxBPM != 0) Console.WriteLine($"Max: {maxBPM} at {maxBPMTime.ToShortTimeString()}".PadRight(32));
            if (minBPM != int.MaxValue) Console.WriteLine($"Min: {minBPM} at {minBPMTime.ToShortTimeString()}".PadRight(32));
            Console.WriteLine($"ConnectionCount: {connectionCount}".PadRight(32));
            if (rrInterval > 0) Console.WriteLine($"RR: {rrInterval}".PadRight(32));
            Console.WriteLine($"Avatar Parameters: {oscSender.AvailableParameterList.Count}".PadRight(32));

            lastUpdate = DateTime.Now;
            if (config.WriteToTxt)
                File.WriteAllText(hrTxtLocation, $"{currentHR}");

            oscSender.Update(currentHR, rrInterval);
            if (!isHeartBeat)
                HeartBeat();
        };

        while (true)
        {
            if (DateTime.Now - lastUpdate > TimeSpan.FromSeconds(config.TimeOutInterval + 2))
            {
                isHRConnected = false;
                oscSender.Clear();
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
                        connectionCount++;
                        Console.Clear();
                        break;
                    }
                    catch (Exception e)
                    {
                        isHRConnected = false;
                        oscSender.Clear();
                        Console.Clear();
                        Console.WriteLine(
                            $"Failure while initiating heartrate service, retrying in {config.RestartDelay} seconds...");
                        Debug.WriteLine(e);
                        Thread.Sleep((int)(config.RestartDelay * 1000));
                    }
                }
            }

            Thread.Sleep(2000);
        }
    }
    
    private static int DefaultWaitTime(int currentHR)
    {
        var waitTime = 1 / ((currentHR - 0.1f) / 60);
        return (int)(waitTime * 1000);
    }

    private static void HeartBeat()
    {
        while (true)
        {
            if (currentHR == 0 || !isHRConnected)
            {
                isHeartBeat = false;
                return;
            }

            isHeartBeat = true;
            var waitTime = rrInterval;
            // If the RR interval is 0, use the old method of calculating the wait time
            if (rrInterval == 0)
                waitTime = DefaultWaitTime(currentHR);

            Task.Delay(waitTime).Wait();
            oscSender.SendBeat();
        }
    }
}