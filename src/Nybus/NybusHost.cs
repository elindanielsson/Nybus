﻿using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nybus.Utils;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nybus.Configuration;

namespace Nybus
{
    public class NybusHost : IBusHost, IBus, IBusExecutionEnvironment
    {
        private readonly IBusEngine _engine;
        private readonly IServiceProvider _serviceProvider;
        private readonly INybusConfiguration _configuration;
        private readonly ILogger<NybusHost> _logger;

        public NybusHost(IBusEngine busEngine, INybusConfiguration configuration, IServiceProvider serviceProvider, ILogger<NybusHost> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _engine = busEngine ?? throw new ArgumentNullException(nameof(busEngine));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public async Task InvokeCommandAsync<TCommand>(TCommand command, Guid correlationId) where TCommand : class, ICommand
        {
            var message = new CommandMessage<TCommand>
            {
                MessageId = Guid.NewGuid().Stringfy(),
                Headers = new HeaderBag
                {
                    CorrelationId = correlationId,
                    SentOn = Clock.Default.Now
                },
                Command = command
            };

            _logger.LogTrace(new { type = typeof(TCommand).FullName, correlationId = correlationId, command }, arg => $"Invoking command of type {arg.type} with correlationId {arg.correlationId}. Command: {arg.command.ToString()}");
            await _engine.SendCommandAsync(message).ConfigureAwait(false);
        }

        public async Task RaiseEventAsync<TEvent>(TEvent @event, Guid correlationId) where TEvent : class, IEvent
        {
            var message = new EventMessage<TEvent>
            {
                MessageId = Guid.NewGuid().Stringfy(),
                Headers = new HeaderBag
                {
                    CorrelationId = correlationId,
                    SentOn = Clock.Default.Now
                },
                Event = @event
            };

            _logger.LogTrace(new { type = typeof(TEvent).FullName, correlationId = correlationId, @event }, arg => $"Raising event of type {arg.type} with correlationId {arg.correlationId}. Event: {arg.@event.ToString()}");
            await _engine.SendEventAsync(message).ConfigureAwait(false);
        }

        private bool _isStarted;
        private IDisposable _disposable;

        public Task StartAsync()
        {
            _logger.LogTrace("Bus starting");

            var incomingMessages = _engine.Start();

            var observable = from message in incomingMessages
                             where message != null
                             from pipeline in _messagePipelines
                             from execution in pipeline(message).ToObservable()
                             select Unit.Default;
        
            _disposable = observable.Subscribe();

            _logger.LogTrace("Bus started");

            _isStarted = true;

            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            if (_isStarted)
            {
                _logger.LogTrace("Bus stopping");

                _engine.Stop();
                _disposable.Dispose();

                _logger.LogTrace("Bus stopped");
            }

            return Task.CompletedTask;
        }

        private readonly List<MessagePipeline> _messagePipelines = new List<MessagePipeline>();

        public void SubscribeToCommand<TCommand>(CommandReceived<TCommand> commandReceived) where TCommand : class, ICommand
        {
            _engine.SubscribeToCommand<TCommand>();

            _messagePipelines.Add(ProcessMessage);

            async Task ProcessMessage(Message message)
            {
                if (message is CommandMessage<TCommand> commandMessage)
                {
                    var context = new NybusCommandContext<TCommand>(commandMessage);

                    try
                    {
                        await ExecuteHandler(context).ConfigureAwait(false);
                        await NotifySuccess(commandMessage).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await HandleError(ex, commandMessage, context).ConfigureAwait(false);
                    }
                }
            }

            async Task ExecuteHandler(ICommandContext<TCommand> context)
            {
                var dispatcher = new NybusDispatcher(this, context.CorrelationId);
                await commandReceived(dispatcher, context).ConfigureAwait(false);
            }

            async Task NotifySuccess(CommandMessage<TCommand> message)
            {
                await _engine.NotifySuccess(message).ConfigureAwait(false);
            }

            async Task HandleError(Exception exception, CommandMessage<TCommand> message, ICommandContext<TCommand> context)
            {
                _logger.LogError(new { CorrelationId = context.CorrelationId, MessageId = message.MessageId, CommandType = typeof(TCommand).Name, Exception = exception, Message = message }, s => $"An error occurred while handling {s.CommandType}. {s.Exception.Message}");
                await _configuration.ErrorPolicy.HandleError(_engine, exception, message).ConfigureAwait(false);
            }
        }

        public void SubscribeToEvent<TEvent>(EventReceived<TEvent> eventReceived) where TEvent : class, IEvent
        {
            _engine.SubscribeToEvent<TEvent>();

            _messagePipelines.Add(ProcessMessage);

            async Task ProcessMessage(Message message)
            {
                if (message is EventMessage<TEvent> eventMessage)
                {
                    var context = new NybusEventContext<TEvent>(eventMessage);

                    try
                    {
                        await ExecuteHandler(context).ConfigureAwait(false);
                        await NotifySuccess(eventMessage).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await HandleError(ex, eventMessage, context).ConfigureAwait(false);
                    }
                }
            }

            async Task ExecuteHandler(IEventContext<TEvent> context)
            {
                var dispatcher = new NybusDispatcher(this, context.CorrelationId);
                await eventReceived(dispatcher, context).ConfigureAwait(false);
            }

            async Task NotifySuccess(EventMessage<TEvent> message)
            {
                await _engine.NotifySuccess(message).ConfigureAwait(false);
            }

            async Task HandleError(Exception exception, EventMessage<TEvent> message, IEventContext<TEvent> context)
            {
                _logger.LogError(new { CorrelationId = context.CorrelationId, MessageId = message.MessageId, EventType = typeof(TEvent).Name, Exception = exception, Message = message }, s => $"An error occurred while handling {s.EventType}. {s.Exception.Message}");
                await _configuration.ErrorPolicy.HandleError(_engine, exception, message).ConfigureAwait(false);
            }
        }

        private delegate Task MessagePipeline(Message message);

        public IBusExecutionEnvironment ExecutionEnvironment => this;

        public async Task ExecuteCommandHandler<TCommand>(IDispatcher dispatcher, ICommandContext<TCommand> context, Type handlerType)
            where TCommand : class, ICommand
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var handler = (ICommandHandler<TCommand>)scope.ServiceProvider.GetRequiredService(handlerType);
                await handler.HandleAsync(dispatcher, context).ConfigureAwait(false);
            }
        }

        public async Task ExecuteEventHandler<TEvent>(IDispatcher dispatcher, IEventContext<TEvent> context, Type handlerType)
            where TEvent : class, IEvent
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var handler = (IEventHandler<TEvent>)scope.ServiceProvider.GetRequiredService(handlerType);
                await handler.HandleAsync(dispatcher, context).ConfigureAwait(false);
            }
        }
    }
}
