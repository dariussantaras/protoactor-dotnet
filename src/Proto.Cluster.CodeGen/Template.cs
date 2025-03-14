﻿// -----------------------------------------------------------------------
// <copyright file="Template.cs" company="Asynkron AB">
//      Copyright (C) 2015-2022 Asynkron AB All rights reserved
// </copyright>
// -----------------------------------------------------------------------

namespace Proto.Cluster.CodeGen;

public static class Template
{
    public const string DefaultTemplate = @"
#nullable enable
#pragma warning disable 1591
using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Proto;
using Proto.Cluster;
using Microsoft.Extensions.DependencyInjection;

namespace {{CsNamespace}}
{
    public static partial class GrainExtensions
    {
        {{#each Services}}
        public static {{Name}}Client Get{{Name}}(this global::Proto.Cluster.Cluster cluster, string identity) => new {{Name}}Client(cluster, identity);

        public static {{Name}}Client Get{{Name}}(this global::Proto.IContext context, string identity) => new {{Name}}Client(context.System.Cluster(), identity);
        {{/each}}
    }

    {{#each Services}}
    public abstract class {{Name}}Base
    {
        protected global::Proto.IContext Context { get; }
        protected global::Proto.ActorSystem System => Context.System;
        protected global::Proto.Cluster.Cluster Cluster => Context.System.Cluster();
    
        protected {{Name}}Base(global::Proto.IContext context)
        {
            Context = context;
        }
        
        public virtual Task OnStarted() => Task.CompletedTask;
        public virtual Task OnStopping() => Task.CompletedTask;
        public virtual Task OnStopped() => Task.CompletedTask;
        public virtual Task OnReceive() => Task.CompletedTask;

        {{#each Methods}}
        public virtual async Task {{Name}}({{LeadingParameterDefinition}}Action{{#if UseReturn}}<{{OutputName}}>{{/if}} respond, Action<string> onError)
        {
            try
            {
                {{#if UseReturn}}
                var res = await {{Name}}({{Parameter}});
                respond(res);
                {{else}}
                await {{Name}}({{Parameter}});
                respond();
                {{/if}}
            }
            catch (Exception x)
            {
                onError(x.ToString());
            }
        }
        {{/each}}
    
        {{#each Methods}}
        public abstract Task{{#if UseReturn}}<{{OutputName}}>{{/if}} {{Name}}({{SingleParameterDefinition}});
        {{/each}}
    }

    public class {{Name}}Client
    {
        private readonly string _id;
        private readonly global::Proto.Cluster.Cluster _cluster;

        public {{Name}}Client(global::Proto.Cluster.Cluster cluster, string id)
        {
            _id = id;
            _cluster = cluster;
        }

        {{#each Methods}}
        public async Task<{{OutputName}}?> {{Name}}({{LeadingParameterDefinition}}CancellationToken ct)
        {
            var gr = new global::Proto.Cluster.GrainRequestMessage({{Index}}, {{#if UseParameter}}{{Parameter}}{{else}}null{{/if}});
            //request the RPC method to be invoked
            var res = await _cluster.RequestAsync<object>(_id, {{../Name}}Actor.Kind, gr, ct);

            return res switch
            {
                // normal response
                {{OutputName}} message => {{#if UseReturn}}message{{else}}global::Proto.Nothing.Instance{{/if}},
                // enveloped response
                global::Proto.Cluster.GrainResponseMessage grainResponse => {{#if UseReturn}}({{OutputName}}?)grainResponse.ResponseMessage{{else}}global::Proto.Nothing.Instance{{/if}},
                // error response
                global::Proto.Cluster.GrainErrorResponse grainErrorResponse => throw new Exception(grainErrorResponse.Err),
                // timeout (when enabled by ClusterConfig.LegacyRequestTimeoutBehavior), othwerwise TimeoutException is thrown
                null => null,
                // unsupported response
                _ => throw new NotSupportedException($""Unknown response type {res.GetType().FullName}"")
            };
        }
        
        public async Task<{{OutputName}}?> {{Name}}({{LeadingParameterDefinition}}global::Proto.ISenderContext context, CancellationToken ct)
        {
            var gr = new global::Proto.Cluster.GrainRequestMessage({{Index}}, {{#if UseParameter}}{{Parameter}}{{else}}null{{/if}});
            //request the RPC method to be invoked
            var res = await _cluster.RequestAsync<object>(_id, {{../Name}}Actor.Kind, gr, context, ct);

            return res switch
            {
                // normal response
                {{OutputName}} message => {{#if UseReturn}}message{{else}}global::Proto.Nothing.Instance{{/if}},
                // enveloped response
                global::Proto.Cluster.GrainResponseMessage grainResponse => {{#if UseReturn}}({{OutputName}}?)grainResponse.ResponseMessage{{else}}global::Proto.Nothing.Instance{{/if}},
                // error response
                global::Proto.Cluster.GrainErrorResponse grainErrorResponse => throw new Exception(grainErrorResponse.Err),
                // timeout (when enabled by ClusterConfig.LegacyRequestTimeoutBehavior), othwerwise TimeoutException is thrown
                null => null,
                // unsupported response
                _ => throw new NotSupportedException($""Unknown response type {res.GetType().FullName}"")
            };
        }
        {{/each}}
    }

    public class {{Name}}Actor : global::Proto.IActor
    {
        public const string Kind = ""{{Kind}}"";

        private {{Name}}Base? _inner;
        private global::Proto.IContext? _context;
        private readonly Func<global::Proto.IContext, global::Proto.Cluster.ClusterIdentity, {{Name}}Base> _innerFactory;
    
        public {{Name}}Actor(Func<global::Proto.IContext, global::Proto.Cluster.ClusterIdentity, {{Name}}Base> innerFactory)
        {
            _innerFactory = innerFactory;
        }

        public async Task ReceiveAsync(global::Proto.IContext context)
        {
            switch (context.Message)
            {
                case Started msg: 
                {
                    _context = context;
                    var id = context.Get<global::Proto.Cluster.ClusterIdentity>()!; // Always populated on startup
                    _inner = _innerFactory(context, id);
                    await _inner.OnStarted();
                    break;
                }
                case Stopping _:
                {
                    await _inner!.OnStopping();
                    break;
                }
                case Stopped _:
                {
                    await _inner!.OnStopped();
                    break;
                }    
                case GrainRequestMessage(var methodIndex, var r):
                {
                    switch (methodIndex)
                    {
                        {{#each Methods}}
                        case {{Index}}:
                        {   
                            {{#if UseParameter}}
                            if (r is {{InputName}} input)
                            {
                                await _inner!.{{Name}}(input, Respond, OnError);
                            }
                            else
                            {
                                OnError($""Invalid client contract. Expected {{InputName}}, received {r?.GetType().FullName}"");
                            }
                            {{else}}
                            await _inner!.{{Name}}(Respond, OnError);
                            {{/if}}

                            break;
                        }
                        {{/each}}
                        default:
                            OnError($""Invalid client contract. Unexpected Index {methodIndex}"");
                            break;
                    }

                    break;
                }
                default:
                {
                    await _inner!.OnReceive();
                    break;
                }
            }
        }

        private void Respond<T>(T response) where T : global::Google.Protobuf.IMessage => _context!.Respond(response is not null ? response : new global::Proto.Cluster.GrainResponseMessage(response));
        private void Respond() => _context!.Respond(new global::Proto.Cluster.GrainResponseMessage(null));
        private void OnError(string error) => _context!.Respond(new global::Proto.Cluster.GrainErrorResponse { Err = error });

        public static global::Proto.Cluster.ClusterKind GetClusterKind(Func<global::Proto.IContext, global::Proto.Cluster.ClusterIdentity, {{Name}}Base> innerFactory)
            => new global::Proto.Cluster.ClusterKind(Kind, global::Proto.Props.FromProducer(() => new {{Name}}Actor(innerFactory)));

        public static global::Proto.Cluster.ClusterKind GetClusterKind<T>(global::System.IServiceProvider serviceProvider, params object[] parameters) where T : {{Name}}Base
            => new global::Proto.Cluster.ClusterKind(Kind, global::Proto.Props.FromProducer(() => new {{Name}}Actor((ctx, id) => global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<T>(serviceProvider, ctx, id, parameters))));
    }
    {{/each}}
}
";
}
