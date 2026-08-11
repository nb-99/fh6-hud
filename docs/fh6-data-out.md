# Forza Horizon 6 "Data Out" Documentation

Snapshot of the official article: <https://support.forza.net/hc/en-us/articles/51744149102611-Forza-Horizon-6-Data-Out-Documentation>
Last synced: 2026-08-04. Update this file if the official article changes.

# Overview

After being configured in-game, telemetry output sends data packets for use by external apps. This one-way UDP traffic is sent to a remote IP address at a rate equal to the game's frame rate. This functionality is also available to the localhost address (127.0.0.1).

A single packet format is sent to the configured address, containing vehicle dynamics, tire data, race status, and player inputs.

# Configuration

The following settings can be configured in-game and are found under SETTINGS > HUD AND GAMEPLAY:

- **Data Out:** Toggles the data output function on and off. When set to On, data will begin to send as soon as the player starts driving.
- **Data Out IP Address:** The target IP address of the remote machine receiving data. The localhost address (127.0.0.1) is supported.
- **Data Out IP Port:** The target IP port of the remote machine receiving data. Be sure your app is listening on the same port and that firewall rules allow data on these ports to be received by your app. **Avoid ports 5200 through 5300**, as the game binds its own outgoing socket to a port in this range.

# Output Structure

## Type Notes

`[Letter][Number]` — the letter defines the type:

- **S** -- Signed Integer
- **U** -- Unsigned Integer
- **F** -- Floating Point

The number defines the amount of bits used.

Examples:
- `S8` is a signed byte with potential values between -128 and 127.
- `F32` is a 32-bit floating point number, equivalent to float/single.
- `U32` is a 32-bit unsigned integer.

## Packet Format

Total packet size: **324 bytes**. All values little-endian.

```c
// = 1 when race is on. = 0 when in menus/race stopped.
S32 IsRaceOn;

// Can overflow to 0 eventually
U32 TimestampMS;

// Engine RPM values
F32 EngineMaxRpm;
F32 EngineIdleRpm;
F32 CurrentEngineRpm;

// In the car's local space; X = right, Y = up, Z = forward
F32 AccelerationX;
F32 AccelerationY;
F32 AccelerationZ;

// In the car's local space; X = right, Y = up, Z = forward
F32 VelocityX;
F32 VelocityY;
F32 VelocityZ;

// Angular velocity in the car's local space (rad/s); X = pitch, Y = yaw, Z = roll
F32 AngularVelocityX;
F32 AngularVelocityY;
F32 AngularVelocityZ;

// Car orientation (radians)
F32 Yaw;
F32 Pitch;
F32 Roll;

// Suspension travel normalized: 0.0f = max stretch; 1.0 = max compression
F32 NormalizedSuspensionTravelFrontLeft;
F32 NormalizedSuspensionTravelFrontRight;
F32 NormalizedSuspensionTravelRearLeft;
F32 NormalizedSuspensionTravelRearRight;

// Tire normalized slip ratio, = 0 means 100% grip and |ratio| > 1.0 means loss of grip.
F32 TireSlipRatioFrontLeft;
F32 TireSlipRatioFrontRight;
F32 TireSlipRatioRearLeft;
F32 TireSlipRatioRearRight;

// Wheel rotation speed radians/sec.
F32 WheelRotationSpeedFrontLeft;
F32 WheelRotationSpeedFrontRight;
F32 WheelRotationSpeedRearLeft;
F32 WheelRotationSpeedRearRight;

// = 1 when wheel is on rumble strip, = 0 when off.
S32 WheelOnRumbleStripFrontLeft;
S32 WheelOnRumbleStripFrontRight;
S32 WheelOnRumbleStripRearLeft;
S32 WheelOnRumbleStripRearRight;

// = 1 when wheel is in a puddle, = 0 when not.
S32 WheelInPuddleFrontLeft;
S32 WheelInPuddleFrontRight;
S32 WheelInPuddleRearLeft;
S32 WheelInPuddleRearRight;

// Non-dimensional surface rumble values passed to controller force feedback
F32 SurfaceRumbleFrontLeft;
F32 SurfaceRumbleFrontRight;
F32 SurfaceRumbleRearLeft;
F32 SurfaceRumbleRearRight;

// Tire normalized slip angle, = 0 means 100% grip and |angle| > 1.0 means loss of grip.
F32 TireSlipAngleFrontLeft;
F32 TireSlipAngleFrontRight;
F32 TireSlipAngleRearLeft;
F32 TireSlipAngleRearRight;

// Tire normalized combined slip, = 0 means 100% grip and |slip| > 1.0 means loss of grip.
F32 TireCombinedSlipFrontLeft;
F32 TireCombinedSlipFrontRight;
F32 TireCombinedSlipRearLeft;
F32 TireCombinedSlipRearRight;

// Actual suspension travel in meters
F32 SuspensionTravelMetersFrontLeft;
F32 SuspensionTravelMetersFrontRight;
F32 SuspensionTravelMetersRearLeft;
F32 SuspensionTravelMetersRearRight;

// Unique ID of the car make/model
S32 CarOrdinal;

// Between 0 (D -- worst cars) and 7 (X class -- best cars) inclusive
S32 CarClass;

// Between 100 (worst car) and 999 (best car) inclusive
S32 CarPerformanceIndex;

// 0 = FWD, 1 = RWD, 2 = AWD
S32 DrivetrainType;

// Number of cylinders in the engine
S32 NumCylinders;

// Car group identifier
U32 CarGroup;

// Velocity loss from smashable object collision (m/s)
F32 SmashableVelDiff;

// Mass of recently hit smashable object (kg)
F32 SmashableMass;

// Position in world space (meters)
F32 PositionX;
F32 PositionY;
F32 PositionZ;

// Speed in meters per second
F32 Speed;

// Power in watts
F32 Power;

// Torque in newton-meters
F32 Torque;

// Tire temperature. The official article does not state the unit; in-game
// observation shows FH6 raw values are Fahrenheit-like (140 raw = 60 C).
F32 TireTempFrontLeft;
F32 TireTempFrontRight;
F32 TireTempRearLeft;
F32 TireTempRearRight;

// Turbo/supercharger boost (PSI above atmospheric)
F32 Boost;

// Fuel level (0.0 = empty, 1.0 = full)
F32 Fuel;

// Total distance traveled (meters)
F32 DistanceTraveled;

// Lap times (seconds); 0.0 if not applicable
F32 BestLap;
F32 LastLap;
F32 CurrentLap;

// Total race time (seconds since driving started)
F32 CurrentRaceTime;

// Number of laps completed
U16 LapNumber;

// Current race position
U8 RacePosition;

// Player inputs (0 to 255)
U8 Accel;
U8 Brake;
U8 Clutch;
U8 HandBrake;

// Current gear
U8 Gear;

// Steering input (-127 = full left, 0 = center, 127 = full right)
S8 Steer;

// Normalized driving line position (-127 to 127)
S8 NormalizedDrivingLine;

// Normalized AI braking difference (-127 to 127)
S8 NormalizedAIBrakeDifference;
```

## Byte offsets

| Offset | Type  | Field                        |
| ------ | ----- | ---------------------------- |
| 0      | S32   | IsRaceOn                     |
| 4      | U32   | TimestampMS                  |
| 8      | F32   | EngineMaxRpm                 |
| 12     | F32   | EngineIdleRpm                |
| 16     | F32   | CurrentEngineRpm             |
| 20     | F32   | AccelerationX                |
| 24     | F32   | AccelerationY                |
| 28     | F32   | AccelerationZ                |
| 32     | F32   | VelocityX                    |
| 36     | F32   | VelocityY                    |
| 40     | F32   | VelocityZ                    |
| 44     | F32   | AngularVelocityX             |
| 48     | F32   | AngularVelocityY             |
| 52     | F32   | AngularVelocityZ             |
| 56     | F32   | Yaw                          |
| 60     | F32   | Pitch                        |
| 64     | F32   | Roll                         |
| 68     | F32   | NormalizedSuspensionTravelFL |
| 72     | F32   | NormalizedSuspensionTravelFR |
| 76     | F32   | NormalizedSuspensionTravelRL |
| 80     | F32   | NormalizedSuspensionTravelRR |
| 84     | F32   | TireSlipRatioFL              |
| 88     | F32   | TireSlipRatioFR              |
| 92     | F32   | TireSlipRatioRL              |
| 96     | F32   | TireSlipRatioRR              |
| 100    | F32   | WheelRotationSpeedFL         |
| 104    | F32   | WheelRotationSpeedFR         |
| 108    | F32   | WheelRotationSpeedRL         |
| 112    | F32   | WheelRotationSpeedRR         |
| 116    | S32   | WheelOnRumbleStripFL         |
| 120    | S32   | WheelOnRumbleStripFR         |
| 124    | S32   | WheelOnRumbleStripRL         |
| 128    | S32   | WheelOnRumbleStripRR         |
| 132    | S32   | WheelInPuddleFL              |
| 136    | S32   | WheelInPuddleFR              |
| 140    | S32   | WheelInPuddleRL              |
| 144    | S32   | WheelInPuddleRR              |
| 148    | F32   | SurfaceRumbleFL              |
| 152    | F32   | SurfaceRumbleFR              |
| 156    | F32   | SurfaceRumbleRL              |
| 160    | F32   | SurfaceRumbleRR              |
| 164    | F32   | TireSlipAngleFL              |
| 168    | F32   | TireSlipAngleFR              |
| 172    | F32   | TireSlipAngleRL              |
| 176    | F32   | TireSlipAngleRR              |
| 180    | F32   | TireCombinedSlipFL           |
| 184    | F32   | TireCombinedSlipFR           |
| 188    | F32   | TireCombinedSlipRL           |
| 192    | F32   | TireCombinedSlipRR           |
| 196    | F32   | SuspensionTravelMetersFL     |
| 200    | F32   | SuspensionTravelMetersFR     |
| 204    | F32   | SuspensionTravelMetersRL     |
| 208    | F32   | SuspensionTravelMetersRR     |
| 212    | S32   | CarOrdinal                   |
| 216    | S32   | CarClass                     |
| 220    | S32   | CarPerformanceIndex          |
| 224    | S32   | DrivetrainType               |
| 228    | S32   | NumCylinders                 |
| 232    | U32   | CarGroup                     |
| 236    | F32   | SmashableVelDiff             |
| 240    | F32   | SmashableMass                |
| 244    | F32   | PositionX                    |
| 248    | F32   | PositionY                    |
| 252    | F32   | PositionZ                    |
| 256    | F32   | Speed (m/s)                  |
| 260    | F32   | Power (W)                    |
| 264    | F32   | Torque (Nm)                  |
| 268    | F32   | TireTempFL                   |
| 272    | F32   | TireTempFR                   |
| 276    | F32   | TireTempRL                   |
| 280    | F32   | TireTempRR                   |
| 284    | F32   | Boost (PSI)                  |
| 288    | F32   | Fuel (0..1)                  |
| 292    | F32   | DistanceTraveled             |
| 296    | F32   | BestLap                      |
| 300    | F32   | LastLap                      |
| 304    | F32   | CurrentLap                   |
| 308    | F32   | CurrentRaceTime              |
| 312    | U16   | LapNumber                    |
| 314    | U8    | RacePosition                 |
| 315    | U8    | Accel                        |
| 316    | U8    | Brake                        |
| 317    | U8    | Clutch                       |
| 318    | U8    | HandBrake                    |
| 319    | U8    | Gear                         |
| 320    | S8    | Steer                        |
| 321    | S8    | NormalizedDrivingLine        |
| 322    | S8    | NormalizedAIBrakeDifference  |
| 323    | --    | (padding to 324 bytes)       |

# Notes

- Data is only sent while the player is actively driving. It is not sent during menus, pauses, replays, rewinds, or after finishing a race.
- Data is transmitted out only (one-way UDP). No data is received by the game.
- The packet format is fixed. Unlike Forza Motorsport, there is no option to select between different formats.
- Forza Horizon 6 includes three fields not present in Forza Motorsport: `CarGroup`, `SmashableVelDiff`, and `SmashableMass`. These are inserted after `NumCylinders` and before `PositionX`.
- Forza Horizon 6 does not include `TireWear` or `TrackOrdinal` fields that are present in Forza Motorsport's "Dash" format.
- FH6 tire-temperature values are observed as Fahrenheit-like raw values. Convert to Celsius with `(raw - 32) * 5 / 9`; for example, raw `140` is `60 C`.
- FH6 observed gear values are `0` for reverse, `1` through `10` for forward gears, and `11` for neutral. Do not learn ratios from values outside `1` through `10`.
