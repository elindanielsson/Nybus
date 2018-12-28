﻿using System;
using System.Threading.Tasks;

namespace Nybus.Configuration {
    public class DelegateWrapperEventHandler<TEvent> : IEventHandler<TEvent>
        where TEvent : class, IEvent
    {
        private readonly EventReceived<TEvent> _handler;

        public DelegateWrapperEventHandler(EventReceived<TEvent> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public Task HandleAsync(IDispatcher dispatcher, IEventContext<TEvent> incomingEvent)
        {
            return _handler.Invoke(dispatcher, incomingEvent);
        }
    }
}