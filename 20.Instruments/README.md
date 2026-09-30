# 20.Instruments: Control room demo (ASP.NET Core)

A process control room built with the sgcHTML .NET instrumentation
components, running on Kestrel. The server simulates a boiler loop (pump,
heater, pressure, temperature, flow, tank level) and pushes every instrument
over WebSocket, so the pages update live with no polling and no page refresh.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8107/.

## Pages

- **Gauges** (`/`, also `/gauges`): circular gauges with 270, 180 and 90
  degree arcs, linear gauges (bar, thermometer, tank), an LED bar, 7 and 14
  segment displays and LED indicators.
- **Panels** (`/panels`): an alarm annunciator (click a flashing tile to
  acknowledge it), an odometer totalizer, VU meters with peak hold, two
  analog clocks and a compass.
- **Controls** (`/controls`): a knob, a toggle switch and a rocker, momentary
  and confirm push buttons, a numeric stepper, a vertical slider and a dual
  thumb slider, plus a gauge, a segment display and LEDs that show how the
  process responds.
- **Trends** (`/trends`): a 3 pen strip chart (2 samples per second) and a 2
  channel oscilloscope (5 frames per second), both canvas instruments fed by
  small push scripts.

## Features

- Every control posts to the server (`POST /ctl/<name>`, the annunciator
  posts `POST /ack`). The server changes the process state and pushes the new
  control position to every browser on the Controls page, and the new
  readings to the Gauges page right away.
- Per-page live channel: the sgcHTMX bridge of each page opens its WebSocket
  on the page URL (accepted by the adapter with `AcceptWebSocketOnAnyPath`),
  and the server writes each live fragment only to the browsers showing that
  page (`ISgcHtmlHub.BroadcastAsync` filtered by the connection path).
- A background service advances the simulation every 500 ms and sends an
  oscilloscope frame every 200 ms.
- Bootstrap, htmx and the sgcHTMX bridge are served locally by the
  sgcHTML.AspNetCore adapter, no CDN.

## Configuration

Kestrel owns the listen port, set in `appsettings.json` (default 8107). The
demo needs no database and no other configuration.

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, the adapter options, the simulation background service |
| `sgcInstrumentsWebHost.cs` | Process simulation, control and acknowledge handlers, per-page push through ISgcHtmlHub |
| `sgcInstrumentsDemo_Pages.cs` | Every instrument, the 4 pages and the live fragments |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
