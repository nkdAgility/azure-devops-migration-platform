// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

public sealed class CompositeProgressSink : IProgressSink
{
    private readonly ILogger<CompositeProgressSink> _logger;
    private readonly IReadOnlyList<IProgressSink> _sinks;

    public CompositeProgressSink(ILogger<CompositeProgressSink> logger, params IProgressSink[] sinks)
    {
        _logger = logger;
        _sinks = sinks.ToList();
    }

    public void Emit(ProgressEvent evt)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                sink.Emit(evt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sink {Sink} threw during Emit", sink.GetType().Name);
            }
        }
    }
}
