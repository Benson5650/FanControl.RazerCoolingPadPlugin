# Razer Laptop Cooling Pad Plugin for **Fan Control**
<p align=center>  <img src="/laptop-cooling-pad-og.webp" width=500/>  </p>
An **unofficial** plugin that adds support for the USB-powered **Razer Laptop Cooling Pad** (VID 0x1532, PID 0x0F43) to [Fan Control](https://github.com/Rem0o/FanControl.Releases).
It exposes:

* **Cooling Pad RPM** – read-only fan sensor.
* **Cooling Pad Curve %** – control sensor (0 – 100 %) mapped linearly to **500-3200 RPM** via the pad’s *Auto-Curve* protocol.

> Why this plugin?  
On the stock firmware (and even Razer Synapse), the Smart Fan Curve is not truly linear.
For example, if you set a range of 50–60 °C → 1000–2000 RPM and the temperature is 55 °C, the pad will jump straight to 2000 RPM instead of interpolating.
This plugin bypasses that behavior by sending direct RPM commands, so you get true linear speed control between 500–3200 RPM.

## Device Support

| Model                                 | USB VID\:PID  | Notes                                                                      |
| ------------------------------------- | ------------- | -------------------------------------------------------------------------- |
| Razer Laptop Cooling Pad| **1532:0F43** | Tested on firmware **v1.10.00_r1**. Other revisions should work – please report! |



## Installation

> **⚠ IMPORTANT**
> *The plugin talks to the pad directly over HID.* Do **not** run it in parallel with Razer Synapse **unless** the pad is set to **Manual** mode there, or Synapse is closed. They will otherwise fight for control.



1. Download the latest **`FanControl.RazerCoolingPad.dll`** from [Releases](https://github.com/Benson5650/FanControl.RazerCoolingPadPlugin/releases).

2. Import `FanControl.RazerCoolingPad.dll` using the `install plugin` card in settings.
   ![Plugin Installation](https://github.com/Rem0o/FanControl.Releases/blob/master/Images/PluginInstallation.png?raw=true)

3. FanControl will automatically refresh.



## Usage

> **⚠ IMPORTANT**
> The cooling pad has **no physical RPM sensor**.
> The “RPM” that Fan Control (and even Razer Synapse) shows is simply the value you last commanded—**not** a measured fan speed.


1. Open **Fan Control**.
   You should see

   * *Cooling Pad RPM* (read-only) and
   * *Cooling Pad Curve %* (control).
2. Add the **Curve %** control to a mix or curve just like any other fan control.

   * 0 % → **500 RPM**
   * 100 % → **3200 RPM**
     Values are rounded to the nearest **50 RPM** step (device limit).
3. When the Cooling Pad is not plugged in, the sensor shows 0 RPM.

