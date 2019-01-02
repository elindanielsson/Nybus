﻿using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
// ReSharper disable InvokeAsExtensionMethod

namespace Nybus
{
    public class InMemoryBusEngine : IBusEngine
    {
        private ISubject<Message> _sequenceOfMessages;
        private bool _isStarted;

        public IObservable<Message> Start()
        {
            _sequenceOfMessages = new Subject<Message>();

            var commands = _sequenceOfMessages
                                .Where(m => m != null)
                                .Where(m => m.MessageType == MessageType.Command)
                                .Cast<CommandMessage>()
                                .Where(m => AcceptedTypes.Contains(m.Type))
                                .Cast<Message>();

            var events = _sequenceOfMessages
                                .Where(m => m != null)
                                .Where(m => m.MessageType == MessageType.Event)
                                .Cast<EventMessage>()
                                .Where(m => AcceptedTypes.Contains(m.Type))
                                .Cast<Message>();

            _isStarted = true;

            return Observable.Merge(commands, events);
        }

        public ISet<Type> AcceptedTypes { get; } = new HashSet<Type>();
        
        public void Stop()
        {
            if (_isStarted)
            {
                _sequenceOfMessages.OnCompleted();
                _sequenceOfMessages = null;
            }
        }

        public Task SendCommandAsync<TCommand>(CommandMessage<TCommand> message) where TCommand : class, ICommand
        {
            if (_isStarted)
            {
                _sequenceOfMessages.OnNext(message);
            }

            return Task.CompletedTask;
        }

        public Task SendEventAsync<TEvent>(EventMessage<TEvent> message) where TEvent : class, IEvent
        {
            if (_isStarted)
            {
                _sequenceOfMessages.OnNext(message);
            }

            return Task.CompletedTask;
        }

        public void SubscribeToCommand<TCommand>() where TCommand : class, ICommand
        {
            AcceptedTypes.Add(typeof(TCommand));
        }

        public void SubscribeToEvent<TEvent>() where TEvent : class, IEvent
        {
            AcceptedTypes.Add(typeof(TEvent));
        }

        public Task NotifySuccess(Message message)
        {
            return Task.CompletedTask;
        }

        public Task NotifyFail(Message message)
        {
            return Task.CompletedTask;
        }
    }
}
