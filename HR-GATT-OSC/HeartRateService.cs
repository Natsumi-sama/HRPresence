using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace HR_GATT_OSC;

internal enum ContactSensorStatus
{
    NotSupported,
    NotSupported2,
    NoContact,
    Contact
}

[Flags]
internal enum HeartRateFlags
{
    None = 0,
    IsShort = 1,
    HasEnergyExpended = 1 << 3,
    HasRRInterval = 1 << 4
}

internal struct HeartRateReading
{
    public HeartRateFlags Flags { get; set; }
    public ContactSensorStatus Status { get; set; }
    public int BeatsPerMinute { get; set; }
    public int? EnergyExpended { get; set; }
    public int[] RRIntervals { get; set; }
}

internal interface IHeartRateService : IDisposable
{
    bool IsDisposed { get; }

    event HeartRateService.HeartRateUpdateEventHandler HeartRateUpdated;

    void InitiateDefault();

    void Cleanup();
}

internal static class MemoryStreamExtensions
{
    public static ushort ReadUInt16(this MemoryStream s)
    {
        return (ushort)(s.ReadByte() | (s.ReadByte() << 8));
    }
}

internal class HeartRateService : IHeartRateService
{
    public delegate void HeartRateUpdateEventHandler(HeartRateReading reading);

    // https://www.bluetooth.com/specifications/gatt/viewer?attributeXmlFile=org.bluetooth.characteristic.heart_rate_measurement.xml
    private const int _heartRateMeasurementCharacteristicId = 0x2A37;

    private static readonly Guid _heartRateMeasurementCharacteristicUuid =
        BluetoothUuidHelper.FromShortId(_heartRateMeasurementCharacteristicId);

    private readonly object _disposeSync = new();
    private byte[] _buffer;

    private GattDeviceService _service;

    public bool IsDisposed { get; private set; }

    public event HeartRateUpdateEventHandler HeartRateUpdated;

    public void InitiateDefault()
    {
        var heartrateSelector = GattDeviceService
            .GetDeviceSelectorFromUuid(GattServiceUuids.HeartRate);

        var devices = DeviceInformation
            .FindAllAsync(heartrateSelector).GetAwaiter().GetResult();

        var device = devices.FirstOrDefault();

        if (device == null)
        {
            throw new ArgumentNullException(
                nameof(device),
                "Unable to locate heart rate device.");
        }

        GattDeviceService service;

        lock (_disposeSync)
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().Name);

            Cleanup();

            service = GattDeviceService.FromIdAsync(device.Id).GetAwaiter().GetResult();

            _service = service;
        }

        if (service == null)
        {
            throw new ArgumentOutOfRangeException(
                $"Unable to get service to {device.Name} ({device.Id}). Is the device inuse by another program? The Bluetooth adaptor may need to be turned off and on again.");
        }

        var heartrate = service
            .GetCharacteristicsForUuidAsync(_heartRateMeasurementCharacteristicUuid)
            .GetAwaiter().GetResult()
            .Characteristics
            .FirstOrDefault();

        if (heartrate == null)
        {
            throw new ArgumentOutOfRangeException(
                $"Unable to locate heart rate measurement on device {device.Name} ({device.Id}).");
        }

        var status = heartrate
            .WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).GetAwaiter().GetResult();

        heartrate.ValueChanged += HeartRate_ValueChanged;

        Debug.WriteLine($"Started {status}");
    }

    public void Cleanup()
    {
        var service = Interlocked.Exchange(ref _service, null);
        service?.Dispose();
    }

    public void Dispose()
    {
        lock (_disposeSync)
        {
            IsDisposed = true;
            Cleanup();
        }
    }

    private void HeartRate_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var buffer = args.CharacteristicValue;
        if (buffer.Length == 0)
            return;

        var byteBuffer = Interlocked.Exchange(ref _buffer, null)
                         ?? new byte[buffer.Length];

        if (byteBuffer.Length != buffer.Length)
        {
            byteBuffer = new byte[buffer.Length];
        }

        try
        {
            using var reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(byteBuffer);

            var readingValue = ReadBuffer(byteBuffer, (int)buffer.Length);

            if (readingValue == null)
            {
                Debug.WriteLine($"Buffer was too small. Got {buffer.Length}.");
                return;
            }

            var reading = readingValue.Value;
            Debug.WriteLine($"Read {reading.Flags:X} {reading.Status} {reading.BeatsPerMinute}");

            HeartRateUpdated?.Invoke(reading);
        }
        finally
        {
            Volatile.Write(ref _buffer, byteBuffer);
        }
    }

    private static HeartRateReading? ReadBuffer(byte[] buffer, int length)
    {
        if (length == 0)
            return null;

        var ms = new MemoryStream(buffer, 0, length);
        var flags = (HeartRateFlags)ms.ReadByte();
        var isShort = flags.HasFlag(HeartRateFlags.IsShort);
        var contactSensor = (ContactSensorStatus)(((int)flags >> 1) & 3);
        var hasEnergyExpended = flags.HasFlag(HeartRateFlags.HasEnergyExpended);
        var hasRRInterval = flags.HasFlag(HeartRateFlags.HasRRInterval);
        var minLength = isShort ? 3 : 2;

        if (buffer.Length < minLength)
            return null;

        var reading = new HeartRateReading
        {
            Flags = flags,
            Status = contactSensor,
            BeatsPerMinute = isShort ? ms.ReadUInt16() : ms.ReadByte()
        };

        if (hasEnergyExpended)
            reading.EnergyExpended = ms.ReadUInt16();

        if (hasRRInterval)
        {
            var rrValueCount = (buffer.Length - ms.Position) / sizeof(ushort);
            var rrValues = new int[rrValueCount];
            for (var i = 0; i < rrValueCount; ++i)
            {
                rrValues[i] = ms.ReadUInt16();
            }

            reading.RRIntervals = rrValues;
        }

        return reading;
    }
}