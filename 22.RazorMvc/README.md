# 22.RazorMvc: sgcHTML in an ASP.NET Core MVC app (Razor)

A classic controllers + Razor views application built with the sgcHTML Razor
layer: Tag Helpers such as `<sgc-grid>`, `<sgc-form>` and `<sgc-chart>`, and
their fluent twins under `Html.Sgc()`. The data lives in an EF Core SQLite
database that is created and seeded on the first run (30 customers, 200
orders). If you know the Telerik UI for ASP.NET Core or DevExpress MVC
samples, this is the same application written with sgcHTML: server rendered
HTML plus htmx, with no client-side framework and no JSON data source.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8111/.

## What it shows

- **Orders**: `<sgc-grid>` over an `IQueryable` projection with server
  paging, sort, text filters, range filters (date and total), global search,
  XLSX and PDF export of the filtered rows, and `push-url`, so the address bar
  keeps page, sort and filters (reload and back button work). Page 1 is
  rendered inline from the view model, every later request goes to
  `Read(SgcDataRequest)`.
- **Edit in a dialog**: the Edit button of each row loads the form into
  `<sgc-dialog name="edit-order">`, and so does any element with the
  `sgc-dialog="edit-order"` attribute. The `<sgc-form hx="true">` holds
  `<sgc-textbox>`, `<sgc-autocomplete>` (customer lookup that posts the key),
  `<sgc-datepicker>`, `<sgc-select>` and `<sgc-checkbox>`, all with
  `asp-for`. An invalid post returns the form with the `ModelState` errors
  under each input; a valid post answers
  `this.SgcCloseDialog("edit-order", refresh: "orders")`, which closes the
  dialog and reloads the grid.
- **New order**: the same form written with the fluent helpers
  (`Html.Sgc().TextBoxFor(...)`, `AutoCompleteFor(...)`, `DatePickerFor(...)`,
  `SelectFor(...)`, `CheckBoxFor(...)`). A valid post answers
  `this.SgcRedirect(url)` and htmx navigates to the orders list, filtered on
  the new order.
- **Customers**: the fluent `Html.Sgc().Grid<CustomerRow>()` builder bound to
  an in memory list with `BindTo`: every row is rendered and sorted in the
  browser, with no data endpoint.
- **Dashboard**: a static chart (sales and orders by month, read from the
  database), a polling chart (`refresh-action` + `refresh-every="5s"`, orders
  by status) and a live chart. The live chart is fed by the `LiveSalesFeed`
  background service, which calls `hub.BroadcastChartAsync(...)` once a
  second; every open browser is updated over the sgcHTML WebSocket, with no
  polling and no SignalR code.

## Coming from Telerik or DevExpress

| Telerik UI for ASP.NET Core / DevExpress | sgcHTML Razor |
|---|---|
| `<kendo-grid>` / `Html.Kendo().Grid<T>()` / `Html.DevExpress().GridView()` | `<sgc-grid>` / `Html.Sgc().Grid<T>()` |
| `<column field="..." />` / `columns.Bound(o => o.X)` | `<sgc-column field="..." />` / `c.Bound(o => o.X)` |
| `[DataSourceRequest] DataSourceRequest request` | `SgcDataRequest request` (bound by `AddSgcHtmlRazor`) |
| `Json(query.ToDataSourceResult(request))` | `this.SgcGrid(request, query)` (HTML fragment), or `query.ToSgcDataResult(request)` for the data only |
| Grid Excel / PDF export (`.Excel()`, `.Pdf()`) | `export="xlsx,pdf"` + `this.SgcGridExport(request, query)` |
| `Html.Kendo().TextBoxFor(m => m.X)` / `<kendo-textbox for="X">` | `Html.Sgc().TextBoxFor(m => m.X)` / `<sgc-textbox asp-for="X">` |
| `DropDownListFor`, `DatePickerFor`, `CheckBoxFor` | `SelectFor`, `DatePickerFor`, `CheckBoxFor` (`<sgc-select>`, `<sgc-datepicker>`, `<sgc-checkbox>`) |
| `ComboBoxFor` / `AutoCompleteFor` with a Read action | `<sgc-autocomplete asp-for search-action>` + `this.SgcAutoComplete(items)` |
| `<kendo-window>` / `PopupControl` + grid popup editing | `<sgc-dialog>` + `sgc-dialog="name"` + `this.SgcCloseDialog(name, refresh)` |
| `<kendo-chart>` / `Html.DevExpress().Chart()` | `<sgc-chart>` + `<sgc-series>` / `Html.Sgc().Chart()` |
| Chart refresh via DataSource + timer / SignalR | `refresh-every="5s"` (polling) or `live="true"` + `hub.BroadcastChartAsync` (push) |

## SQLite notes

- Money is a `double`: EF Core SQLite cannot `ORDER BY` a `decimal` column,
  so a decimal Total would break the grid sort. Use `decimal` freely on SQL
  Server, PostgreSQL or MySQL.
- EF Core translates `string.Contains` to SQLite `instr()`, which is case
  sensitive, so the grid text filters and the search match case: type `Acme`,
  not `acme`. The customer autocomplete uses `EF.Functions.Like`, which SQLite
  compares case insensitively for ASCII. Other databases follow their
  collation.
- The database file is `orders.db` next to the project; delete it to start
  again with the seed data.

## Configuration

The listen port (default 8111) and the connection string come from
`appsettings.json`. The app uses one fixed culture (`en-US`), so numbers and
dates post the same on any machine.

## Project layout

| File | Role |
|---|---|
| `Program.cs` | Services (`AddSgcHtml`, `AddSgcHtmlRazor`, EF Core), seed, pipeline |
| `Data/ShopDb.cs` | Entities, `DbContext` and seed data |
| `Data/LiveSalesFeed.cs` | Background service that pushes the live chart |
| `Models/OrderModels.cs` | Grid rows, the order form model and the dashboard model |
| `Controllers/OrdersController.cs` | Grid read and export, autocomplete, edit dialog, create |
| `Controllers/CustomersController.cs` | Customer list for the fluent grid |
| `Controllers/DashboardController.cs` | Static chart data and the polled chart |
| `Views/Shared/_Layout.cshtml` | `<sgc-styles />`, `<sgc-scripts live="ws" />` and the menu |
| `Views/Orders/*.cshtml` | Grid page, edit form (Tag Helpers), create form (fluent helpers) |
| `Views/Customers/Index.cshtml` | Fluent grid with `BindTo` |
| `Views/Dashboard/Index.cshtml` | Static, polling and live charts |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
