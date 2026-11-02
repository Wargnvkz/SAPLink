# SAPLink

A lightweight wrapper around the **SAP .NET Connector (NCo 3.x)**. Call function modules and BAPIs and read tables using plain C# classes instead of hand-filling `IRfcStructure` / `IRfcTable`.

- C# classes map to SAP structures and tables **by name** (case-insensitive), no per-field attributes needed for function calls
- `GetTable<T>` reads tables via `RFC_READ_TABLE`, with WHERE conditions written as a string **or as a C# lambda**
- **Explicit transactions** for BAPIs: a forgotten `Commit` means a rollback, not a hanging session
- Several connections (different systems or users) in one process work out of the box

> SAPLink does **not** include SAP NCo. See [Requirements](#requirements).

## Requirements

- Windows, .NET Framework 4.8 / .NET *(TODO: list the versions you actually tested)*
- **SAP .NET Connector 3.x** (`sapnco.dll`, `sapnco_utils.dll`), tested with version *(TODO: e.g. 3.0.x / 3.1.x)*

NCo is proprietary and SAP's license does not allow redistributing it, so it is not part of this repository. Download it from the SAP Support Portal (an S-user is required), choose the **x86 or x64** package that matches your application, and put the DLLs next to your executable.

### 32-bit vs 64-bit

SAPLink is built as **AnyCPU**. The bitness is decided by your application:

1. Download the NCo package (x86 *or* x64) that matches the application.
2. Set the application's *Platform target* to `x86` or `x64` (not `Any CPU`, or uncheck *Prefer 32-bit*).

If you get `BadImageFormatException` or a load error for `sapnco.dll`, the bitness of the application and of NCo do not match.

## Installation

*(TODO: NuGet package name, or "clone and reference the project")*

Reference `sapnco.dll` and `sapnco_utils.dll` from your own project. SAPLink references them with `Copy Local = false`.

## Quick start

```csharp
using SAPLink;
using SAPLink.Classes;

var logon = new Logon(
    "sap-host",   // host
    "00",         // system number
    "PRD",        // system ID
    "USER",       // user
    "password",   // password (read it from a secret store or environment, not from code)
    "100",        // client
    "EN",         // language
    "5");         // connection pool size

using (var sap = new SAPLink.SAPLink(logon))
{
    if (sap.TryPing(out var error) != PingError.None)
    {
        Console.WriteLine($"Connection failed: {error}");
        return;
    }

    var workplaces = sap.GetTable<SAPWorkplace>("CRHD", "ARBPL IN ( 'WP1', 'WP2' )");
}

public class SAPWorkplace
{
    [SAPGetTable("OBJID")] public string WorkplaceID;     // name in SAP differs from the C# name
    [SAPGetTable("ARBPL")] public string WorkplaceName;
}
```

Always wrap `SAPLink` in `using` (or call `Dispose`). This closes the connection pool and releases the destination. A finalizer exists as a safety net only.

## Reading tables

`GetTable<T>` uses the standard function module `RFC_READ_TABLE`. Fields of class `T` are matched to table columns:

- the field name is used as the SAP column name by default;
- `[SAPGetTable("NAME")]` sets the SAP column name explicitly;
- `[SAPExcludeFromReading]` skips a field;
- only **public fields** are read;
- dates (`yyyyMMdd`) and times (`hhmmss`) can be converted to `DateTime` / `TimeSpan`;
- one class can describe several field sets ("variants") and the variant number selects which one is read; `-1` reads all of them.

### Conditions as a string

```csharp
var rows = sap.GetTable<SAPWorkplace>("CRHD", "WERKS = '1000' AND ARBPL <> 'WP9'");
```

Rules of ABAP conditions:

- tokens are separated by **spaces** (`'A','B'` without spaces is rejected by SAP; write `( 'A', 'B' )`);
- string literals are in single quotes, a quote inside a literal is doubled (`''`);
- numeric-text fields (e.g. material numbers, order numbers) need **leading zeros**: `'000000000000000001'`, not `'1'`;
- the condition is split into lines of up to 60 characters automatically at spaces outside of quotes; a condition that cannot be split throws an exception.

### Conditions as a lambda

```csharp
var plant = "1000";
var orders = new List<string> { "0000100001", "0000100002" };

var rows = sap.GetTable<SAPOrder>("AUFK", -1,
    o => orders.Contains(o.OrderId) && o.Plant == plant);
// ( AUFNR IN ( '0000100001', '0000100002' ) AND WERKS = '1000' )
```

The lambda is translated one-to-one into an ABAP condition. Anything that cannot be translated throws `NotSupportedException`, nothing is silently changed.

Supported: `==`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `||`, `list.Contains(field)` (becomes `IN`), `!list.Contains(field)` (becomes `NOT IN`). The left side of a comparison must be a field of the row. Values may be constants or local variables.

To see what will be sent to SAP:

```csharp
Console.WriteLine(AbapWhere.Build<SAPOrder>(o => o.Plant == "1000"));
```

### Limits of `RFC_READ_TABLE`

These come from SAP, not from SAPLink:

- the total width of the selected fields in one row is limited (about 512 characters); select fewer fields for wide tables;
- one condition line is limited to 72 characters (SAPLink splits at 60);
- not every field type is supported (for example, long strings);
- no paging: the whole result is read in one call;
- access can be restricted by authorizations (`S_TABU_DIS`) or the function module may be disabled in your system. If so, use a custom function module with `CallFunction`.

### Long lists in `Contains`

A huge `IN` list produces a very long condition. Limits depend on your SAP system and database, so read in chunks and merge:

```csharp
var result = new List<SAPOrder>();
for (int i = 0; i < orders.Count; i += 200)
{
    var chunk = orders.Skip(i).Take(200).ToList();
    result.AddRange(sap.GetTable<SAPOrder>("AUFK", -1, o => chunk.Contains(o.OrderId)));
}
```

An empty list in `Contains` throws an exception (`IN ( )` is invalid in ABAP). Check for an empty list before the call.

## Calling function modules

Input and output are described with ordinary classes. Names are matched case-insensitively. Two keys that differ only by case throw an exception: SAP names are case-insensitive, so this is treated as a caller error.

```csharp
var data = new Dictionary<string, object>
{
    ["IV_MATERIAL"] = "000000000000000001"
};
var output = new Dictionary<string, Type>
{
    ["ES_DATA"] = typeof(MaterialData),
    ["ET_ITEMS"] = typeof(MaterialItem)      // tables are returned as List<MaterialItem>
};

var result = sap.CallFunction("Z_GET_MATERIAL", data, output);
var header = (MaterialData)result["ES_DATA"];
var items  = (List<MaterialItem>)result["ET_ITEMS"];
```

SAP exceptions are thrown as exceptions. Errors that a BAPI returns in its `RETURN` table are **not** exceptions: check them yourself (see below).

## BAPIs and transactions

A BAPI call and the following `BAPI_TRANSACTION_COMMIT` must run in the **same SAP session**. `BeginTransaction` takes care of that. If you do not call `Commit`, the transaction is rolled back on `Dispose`.

```csharp
using (var tx = sap.BeginTransaction())
{
    var result = tx.Call("BAPI_GOODSMVT_CREATE", data, output);

    var returns = (List<BAPIRETURN2>)result["RETURN"];
    var error = returns.FirstOrDefault(r => r.TYPE == "E" || r.TYPE == "A");

    if (error != null)
    {
        tx.Rollback();
        Console.WriteLine(error.MESSAGE);
    }
    else
    {
        tx.Commit();               // BAPI_TRANSACTION_COMMIT with WAIT = 'X'
    }
}
```

Several BAPI calls can be made inside one transaction before a single `Commit`.

Rules:

- use one thread from `BeginTransaction` to `Commit` / `Dispose`; **no `await`** inside the transaction block (the SAP session is bound to the thread);
- reading (`GetTable`, read-only function modules) does not need a transaction;
- whether to commit is decided by you, after checking `RETURN`. The type of messages (`E`, `A`, `W`...) depends on the BAPI.

A complete runnable example is in [`examples/`](examples/).

## Several connections in one process

Each `SAPLink` gets its own destination name and its own connection pool. Different systems or users can be used at the same time:

```csharp
using (var prd = new SAPLink.SAPLink(prdLogon))
using (var qas = new SAPLink.SAPLink(qasLogon))
{
    // independent connections
}
```

NCo allows only one destination configuration per process; SAPLink registers a single shared one and manages it for you. If your host application has already registered its own `IDestinationConfiguration`, SAPLink cannot register and will throw `RfcInvalidStateException`.

## Debugging notes

You may see `System.NullReferenceException` in `sapnco.dll` in the debugger (Output window) when registering a destination. It is a first-chance exception thrown and handled inside NCo and does not affect the application. To hide it, enable *Just My Code* or exclude `sapnco.dll` in *Exception Settings*.

## Differences from other libraries

There are other wrappers and alternatives: for example SharpSapRfc, NSAPConnector, RfcClient, and YaNco (an independent implementation on top of the SAP NetWeaver RFC SDK, not a wrapper around NCo). SAPLink aims at: mapping by name to ordinary classes, explicit BAPI transactions, and conditions written as lambdas. Check the others if you need something different, for example a replacement for NCo itself.

## Roadmap

- attribute for explicit SAP field names in function-call mapping (`CallFunction`)
- leading-zero handling (ALPHA conversion) for key fields
- `LIKE` for `StartsWith`
- out-of-process host for 32-bit NCo from 64-bit applications

## License

*(TODO: MIT is common; add a `LICENSE` file.)* SAP NCo is not covered by this license and is subject to SAP's terms.
