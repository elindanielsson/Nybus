﻿using System;
using System.Threading.Tasks;
using Nybus;
using Microsoft.Extensions.Logging;

namespace NetCoreConsoleApp
{
    public class TestEvent : IEvent
    {
        public string Message { get; set; }
    }

    public class TestEventHandler : IEventHandler<TestEvent>
    {
        private ILogger<TestEventHandler> _logger;

        public TestEventHandler(ILogger<TestEventHandler> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.LogTrace($"ctor: {nameof(TestEventHandler)}");
        }

        public Task HandleAsync(IBusDispatcher bus, IEventContext<TestEvent> incomingEvent)
        {
            _logger.LogInformation(incomingEvent.Event.Message);

            return Task.CompletedTask;
        }
    }
}