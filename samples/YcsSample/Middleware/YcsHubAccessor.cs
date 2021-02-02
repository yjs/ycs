using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using YcsSample.Hubs;

namespace YcsSample.Middleware
{
    public class YcsHubAccessor
    {
        private static readonly Lazy<YcsHubAccessor> _instance = new Lazy<YcsHubAccessor>(() => new YcsHubAccessor());

        private YcsHubAccessor()
        {
            // Do nothing.
        }

        public static YcsHubAccessor Instance => _instance.Value;

        public IHubContext<YcsHub> YcsHub { get; internal set; }
    }

    public static class YcsHubAccessorMiddlewareExtensions
    {
        public static IApplicationBuilder UseYcsHubAccessor(this IApplicationBuilder appBuilder)
        {
            return appBuilder.Use(async (context, next) =>
            {
                YcsHubAccessor.Instance.YcsHub = context.RequestServices.GetRequiredService<IHubContext<YcsHub>>();

                if (next != null)
                {
                    await next.Invoke();
                }
            });
        }
    }
}
