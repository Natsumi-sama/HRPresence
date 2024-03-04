# HRPresence

Windows GATT heartrate monitor tool that pushes BPM to OpenSoundControl (OSC) for VRChat or ChilloutVR.
Simply connect any generic Bluetooth heartrate monitor to your computer and run the application!

```toml
# config.toml

# Restart if x seconds of no communication
time_out_interval = 3.0

# Wait x seconds before restarting in case of any errors
restart_delay = 3.0

# Write HR value to HR.txt
write_to_txt = false

# Support Quest in standalone mode
quest_standalone = false
```

## OSC Parameters

| Parameter         | Type    | Path                                 | Description                                        |
| ----------------- | ------- | ------------------------------------ | -------------------------------------------------- |
| `HR`              | `int`   | `/avatar/parameters/HR`              | actual heartrate value                             |
| `onesHR`          | `int`   | `/avatar/parameters/onesHR`          | ones digit                                         |
| `tensHR`          | `int`   | `/avatar/parameters/tensHR`          | tens digit                                         |
| `hundredsHR`      | `int`   | `/avatar/parameters/hundredsHR`      | hundreds digit                                     |
| `floatHR`         | `float` | `/avatar/parameters/floatHR`         | maps 0:255 to -1.0:1.0                             |
| `isHRBeat`        | `bool`  | `/avatar/parameters/isHRBeat`        | set when heart beats                               |
| `HeartBeatToggle` | `bool`  | `/avatar/parameters/HeartBeatToggle` | flip flops every heart beat                        |
| `isHRConnected`   | `bool`  | `/avatar/parameters/isHRConnected`   | set when HR monitor connected                      |
| `RRInterval`      | `int`   | `/avatar/parameters/RRInterval`      | heart beat interval int in ms (only for debugging) |
