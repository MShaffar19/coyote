---
layout: reference
section: learn
title: ILogger
permalink: /learn/ref/Microsoft.Coyote.IO/ILoggerType
---
# ILogger interface

A logger is used to capture messages, warnings and errors.

```csharp
public interface ILogger : IDisposable
```

## Members

| name | description |
| --- | --- |
| [TextWriter](ILogger/TextWriter) { get; } | This property provides a TextWriter that implements ILogger which is handy if you have existing code that requires a TextWriter. |
| [Write](ILogger/Write)(…) | Writes an informational string to the log. (4 methods) |
| [WriteLine](ILogger/WriteLine)(…) | Writes an informational string to the log. (4 methods) |

## See Also

* namespace [Microsoft.Coyote.IO](../MicrosoftCoyoteIONamespace)
* assembly [Microsoft.Coyote](../MicrosoftCoyoteAssembly)

<!-- DO NOT EDIT: generated by xmldocmd for Microsoft.Coyote.dll -->