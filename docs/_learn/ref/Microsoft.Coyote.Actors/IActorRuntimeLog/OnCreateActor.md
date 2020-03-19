---
layout: reference
section: learn
title: OnCreateActor
permalink: /learn/ref/Microsoft.Coyote.Actors/IActorRuntimeLog/OnCreateActor
---
# IActorRuntimeLog.OnCreateActor method

Invoked when the specified actor has been created.

```csharp
public void OnCreateActor(ActorId id, ActorId creator)
```

| parameter | description |
| --- | --- |
| id | The id of the actor that has been created. |
| creator | The id of the creator, or null. |

## See Also

* class [ActorId](../ActorIdType)
* interface [IActorRuntimeLog](../IActorRuntimeLogType)
* namespace [Microsoft.Coyote.Actors](../IActorRuntimeLogType)
* assembly [Microsoft.Coyote](../../MicrosoftCoyoteAssembly)

<!-- DO NOT EDIT: generated by xmldocmd for Microsoft.Coyote.dll -->