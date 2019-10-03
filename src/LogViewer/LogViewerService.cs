﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tools.RuntimeClient;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogViewer
{
    public class LogViewerService : BackgroundService
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly LogViewerServiceOptions _options;
        private readonly IHostApplicationLifetime _lifetime;
        private static readonly string _MicrosoftExtensionsLoggingProviderName = "Microsoft-Extensions-Logging";
        private readonly IDictionary<string, ILogger> loggerCache = new Dictionary<string, ILogger>();
        public LogViewerService(ILoggerFactory loggerFactory, IOptions<LogViewerServiceOptions> options, IHostApplicationLifetime applicationLifetime)
        {
            _loggerFactory = loggerFactory;
            _options = options.Value;
            _lifetime = applicationLifetime;
        }
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            var providerList = new List<Provider>()
            {
                new Provider(name: _MicrosoftExtensionsLoggingProviderName,
                             keywords: (ulong)LoggingEventSource.Keywords.FormattedMessage,
                             eventLevel: EventLevel.LogAlways)
            };
            var configuration = new SessionConfigurationV2(
                    circularBufferSizeMB: 1000,
                    format: EventPipeSerializationFormat.NetTrace,
                    requestRundown: false,
                    providers: providerList);

            var binaryReader = EventPipeClient.CollectTracing(_options.ProcessId, configuration, out var sessionId);
            var source = new EventPipeEventSource(binaryReader);
            source.Dynamic.AddCallbackForProviderEvent(_MicrosoftExtensionsLoggingProviderName, "FormattedMessage", (traceEvent) =>
            {
                // Level, FactoryID, LoggerName, EventID, EventName, FormattedMessage
                var categoryName = (string)traceEvent.PayloadValue(2);
                if (!loggerCache.ContainsKey(categoryName))
                {
                    loggerCache.TryAdd(categoryName, _loggerFactory.CreateLogger(categoryName));
                }
                if (loggerCache.TryGetValue(categoryName, out var logger))
                {
                    var logLevel = (LogLevel)traceEvent.PayloadValue(0);
                    switch(logLevel)
                    {
                        case LogLevel.Trace:
                            logger.LogTrace((string)traceEvent.PayloadValue(4));
                            break;
                        case LogLevel.Debug:
                            logger.LogDebug((string)traceEvent.PayloadValue(4));
                            break;
                        case LogLevel.Information:
                            logger.LogInformation((string)traceEvent.PayloadValue(4));
                            break;
                        case LogLevel.Warning:
                            logger.LogWarning((string)traceEvent.PayloadValue(4));
                            break;
                        case LogLevel.Error:
                            logger.LogError((string)traceEvent.PayloadValue(4));
                            break;
                        case LogLevel.Critical:
                            logger.LogCritical((string)traceEvent.PayloadValue(4));
                            break;
                    }
                }
            });
            source.Process();
            _lifetime.StopApplication();
        }

    }


}