# 21.SCADA_IoT: SCADA + IoT demo (ASP.NET Core)

A cooling water plant HMI rendered server-side with sgcHTML .NET and running
on Kestrel: a SCADA mimic (tanks, valves, pump, motor, fan, heat exchanger,
sensors and animated pipes), a flow gauge, a thermometer, a strip chart trend
and an alarm annunciator. A simulated PLC scans the process every second and
every change is pushed live to all connected browsers over the WebSocket.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8110 (default port 8110).

## Features

- SCADA panel (`TsgcHTMLComponent_SCADAPanel`): click a valve (V1, V2, V3),
  the pump (P1) or the fan (F1) to toggle it. Valves travel through a
  `transit` state for two PLC scans before they report `open` or `closed`.
- Live process: tank levels, pipe flow animation, pressure, temperature and
  flow sensors, all updated in place with out-of-band fragments.
- Flow gauge, HX1 outlet thermometer and a 2 minute strip chart trend
  (supply, outlet and flow pens).
- Annunciator (`TsgcHTMLComponent_Annunciator`): high / low level, high
  temperature, overpressure, pump fault and low flow alarms. Click a flashing
  tile to acknowledge it. Close V2 to see the blocked discharge overpressure,
  and after six scans without flow the pump trips to fault.
- Every browser sees the same plant: a click in one window updates all others.

## Simulate mode only

The .NET console demo (`demos\60.HTML\21.SCADA_IoT`) can also run in broker
mode: the PLC and the HMI talk MQTT through a public broker
(test.mosquitto.org), and `TsgcHTMLInstrumentsMQTTBinder` maps the MQTT
topics onto the components. This ASP.NET Core version always runs in
simulate mode, because:

- `esegece.sgcHTML.AspNetCore` is a self-contained assembly. It does not
  contain the MQTT binder (compiled only outside the standalone build) nor an
  MQTT client.
- It cannot be referenced together with `esegece.sgcWebSockets` or
  `esegece.sgcHTML` (which do contain them): both define the same public types
  (error CS0433, and the package's SGCHTML001 build guard).

The topic to component mapping does not need MQTT, though. The assembly
contains `TsgcHTMLInstrumentsBinder`, the transport-free base class of the
MQTT binder, and `sgcSCADA_Plant.cs` uses it with the same bindings as the
console demo:

- The simulated PLC hands each topic and payload pair it would publish (for
  example `.../valve/V1/state` = `transit`, `.../sensor/TT1` =
  `{"raw":1374}`) to `ProcessMessage`, the same path an MQTT PUBLISH takes in
  the MQTT binder. The binder applies the JSON path, scale and offset,
  updates the component and renders its live fragment.
- The fragments leave through the binder `OnBroadcast` event (a .NET only
  event of the binder). The handler hands them to the web host, which sends
  them to the browsers with `ISgcHtmlHub.BroadcastAsync`. Overriding
  `DoBroadcast` in a subclass works too.
- `ProcessMessage` is thread safe; the demo also runs it under the page lock
  so a page render never sees a half-updated panel.

## Configuration

The listen port comes from `appsettings.json` (default 8110).
`sgcSCADAServer.conf.json` has the same layout as the console demo; only
`mqtt.topicPrefix` is used here (empty = a unique `sgc/scada-XXXXXXXX` root
per run). A `mode` other than `simulate` is reported on the console and
ignored.

## Endpoints

| Route | Role |
|---|---|
| `GET /` | The dashboard (a `TsgcHTMLComponent_Site` shell) |
| `POST /cmd` | SCADA symbol click (`symbol` = V1, V2, V3, P1 or F1), answers 204 |
| `POST /ack` | Annunciator tile acknowledge (`tile` = tile id), answers 204 |
| `/ws` | The live channel (served by the sgcHTML adapter) |

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, sgcHTML services, PLC start / stop |
| `sgcSCADAWebHost.cs` | Page, /cmd and /ack handlers, push through the hub |
| `sgcSCADA_Plant.cs` | HMI components, binder bindings and simulated PLC |
| `sgcSCADAServer.conf.json` | Topic root |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
