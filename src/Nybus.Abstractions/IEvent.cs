﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Nybus
{
    public interface IEvent { }

    public interface IEventHandler<TEvent> where TEvent : class, IEvent
    {
        Task HandleAsync(IEventContext<TEvent> incomingEvent);
    }
    
    public interface IEventContext<TEvent> where TEvent : class, IEvent
    {
        TEvent Event { get; }

        DateTimeOffset ReceivedOn { get; }

        Guid CorrelationId { get; }
    }
}