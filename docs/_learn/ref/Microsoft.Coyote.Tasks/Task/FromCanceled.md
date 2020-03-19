---
layout: reference
section: learn
title: FromCanceled
permalink: /learn/ref/Microsoft.Coyote.Tasks/Task/FromCanceled
---
# Task.FromCanceled method (1 of 2)

Creates a [`Task`](../TaskType) that is completed due to cancellation with a specified cancellation token.

```csharp
public static Task FromCanceled(CancellationToken cancellationToken)
```

| parameter | description |
| --- | --- |
| cancellationToken | The cancellation token with which to complete the task. |

## Return Value

The canceled task.

## See Also

* class [Task](../TaskType)
* namespace [Microsoft.Coyote.Tasks](../TaskType)
* assembly [Microsoft.Coyote](../../MicrosoftCoyoteAssembly)

---

# Task.FromCanceled&lt;TResult&gt; method (2 of 2)

Creates a [`Task`](../Task-1Type) that is completed due to cancellation with a specified cancellation token.

```csharp
public static Task<TResult> FromCanceled<TResult>(CancellationToken cancellationToken)
```

| parameter | description |
| --- | --- |
| TResult | The type of the result returned by the task. |
| cancellationToken | The cancellation token with which to complete the task. |

## Return Value

The canceled task.

## See Also

* class [Task&lt;TResult&gt;](../Task-1Type)
* class [Task](../TaskType)
* namespace [Microsoft.Coyote.Tasks](../TaskType)
* assembly [Microsoft.Coyote](../../MicrosoftCoyoteAssembly)

<!-- DO NOT EDIT: generated by xmldocmd for Microsoft.Coyote.dll -->