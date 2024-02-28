using System.Net;
using System.Net.Sockets;
using OscCore;

namespace HR_GATT_OSC;

internal class OscService
{
    private UdpClient? _udp;
    
    private readonly HashSet<string> _parameterList = new()
    {
        "isHRConnected",
        "HR",
        "onesHR",
        "tensHR",
        "hundredsHR",
        "floatHR",
        "isHRBeat",
        "RRInterval",
        "HeartBeatToggle"
    };
    
    private readonly HashSet<string> _availableParameterList = new();
    private readonly List<OscMessage> _oscMessages = new();
    private bool _heartBeatToggle;

    public void Initialize(IPAddress ip, int port)
    {
        _udp?.Dispose();
        _udp = new UdpClient();
        _udp.Connect(ip, port);
    }

    public bool Update(int heartRate, int rrInterval)
    {
        if (_availableParameterList.Count == 0 || _udp == null) 
            return true;
        
        // Maps the heart rate from [0;255] to [-1;+1]
        var floatHr = heartRate * 0.0078125f - 1.0f;
        var data = new (string, object)[]
        {
            ("isHRConnected", Program.isHRConnected),
            ("HR", heartRate),
            ("onesHR", heartRate % 10),
            ("tensHR", heartRate / 10 % 10),
            ("hundredsHR", heartRate / 100 % 10),
            ("floatHR", floatHr),
            ("RRInterval", rrInterval)
        };

        try
        {
            _oscMessages.Clear();
            foreach (var (path, value) in data)
            {
                if (!_availableParameterList.Contains(path))
                    continue;

                _oscMessages.Add(new OscMessage($"/avatar/parameters/{path}", value));
            }
            var bytes = new OscBundle(DateTime.Now, _oscMessages.ToArray()).ToByteArray();
            _udp.Send(bytes, bytes.Length);
        }
        catch
        {
            return false;
        }

        return true;
    }

    public void Clear()
    {
        if (_availableParameterList.Count == 0 || _udp == null) 
            return;
        
        var data = new (string, object)[]
        {
            ("isHRConnected", false),
            ("HR", 0),
            ("onesHR", 0),
            ("tensHR", 0),
            ("hundredsHR", 0),
            ("floatHR", -1f),
            ("isHRBeat", false),
            ("RRInterval", 0)
        };
        try
        {
            _oscMessages.Clear();
            foreach (var (path, value) in data)
            {
                if (!_availableParameterList.Contains(path))
                    continue;

                _oscMessages.Add(new OscMessage($"/avatar/parameters/{path}", value));
            }
            var bytes = new OscBundle(DateTime.Now, _oscMessages.ToArray()).ToByteArray();
            _udp.Send(bytes, bytes.Length);
        }
        catch
        {
            // ignored
        }
    }

    public void SendBeat()
    {
        if (_availableParameterList.Count == 0 || _udp == null) 
            return;
        
        if (_availableParameterList.Contains("HeartBeatToggle"))
        {
            _heartBeatToggle = !_heartBeatToggle;
            try
            {
                var bytes = new OscMessage("/avatar/parameters/HeartBeatToggle", _heartBeatToggle).ToByteArray();
                _udp.Send(bytes, bytes.Length);
            }
            catch
            {
                // ignored
            }
        }
        
        if (!_availableParameterList.Contains("isHRBeat"))
            return;
        
        try
        {
            var bytes = new OscMessage("/avatar/parameters/isHRBeat", true).ToByteArray();
            _udp.Send(bytes, bytes.Length);
            Thread.Sleep(250); // needs to be high enough to compensate for the comically low network update rate
            var bytes1 = new OscMessage("/avatar/parameters/isHRBeat", false).ToByteArray();
            _udp.Send(bytes1, bytes1.Length);
        }
        catch
        {
            // ignored
        }
    }
    
    public void UpdateAvailableParameters(Dictionary<string, object?> parameterList)
    {
        _availableParameterList.Clear();
        foreach (var parameter in parameterList.Keys)
        {
            var parameterName = parameter.Replace("/avatar/parameters/", "");
            if (_parameterList.Contains(parameterName))
                _availableParameterList.Add(parameterName);
        }
        
        Console.WriteLine($"Found {_availableParameterList.Count} parameters");
    }
}