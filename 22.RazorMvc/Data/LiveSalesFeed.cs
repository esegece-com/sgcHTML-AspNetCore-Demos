using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
// sgc
using esegece.sgcWebSockets.AspNetCore;
using esegece.sgcWebSockets.AspNetCore.Razor;

namespace RazorMvcDemo.Data
{
    /// <summary>
    /// Simulates the orders per second of a shop and pushes the last 20 points to
    /// every browser that shows the chart named "live-orders" (live="true").
    /// No polling and no SignalR: one WebSocket message updates every open chart.
    /// </summary>
    public sealed class LiveSalesFeed : BackgroundService
    {
        private const int CS_POINTS = 20;
        private readonly ISgcHtmlHub FHub;
        private readonly Queue<double> FValues = new Queue<double>();
        private readonly Queue<string> FLabels = new Queue<string>();

        public LiveSalesFeed(ISgcHtmlHub hub)
        {
            FHub = hub;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var oRandom = new Random();
            double vValue = 10;
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
                vValue = Math.Max(0, vValue + oRandom.Next(-3, 4));
                FValues.Enqueue(vValue);
                FLabels.Enqueue(DateTime.Now.ToString("HH:mm:ss"));
                if (FValues.Count > CS_POINTS)
                {
                    FValues.Dequeue();
                    FLabels.Dequeue();
                }

                await FHub.BroadcastChartAsync("live-orders", FLabels.ToArray(),
                    new SgcChartSeries("Orders / s", FValues.ToArray(), "#dc3545"));
            }
        }
    }
}
