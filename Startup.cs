using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyWebApi
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            // Configure the Forwarded Headers options. 
            // In the options, we can also restrict KnownProxies and KnownNetworks for security purposes when the app is in production.
            services.Configure<ForwardedHeadersOptions>(options => {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // calls the middleware to run first in the pipeline, 
            // so that the forwarded headers information is available for other middlewares later in the pipeline
            app.UseForwardedHeaders();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                // We can add some debugging code to check if everything works or not. 
                // The following code snippet sets up the application root page to show the host information, 
                // HTTP request scheme/path/headers, and the remote IP address.
                endpoints.MapGet("/", async context =>
                {
                    context.Response.ContentType = "text/plain";

                    // Host info
                    var name = Dns.GetHostName(); // get container id
                    var ip = Dns.GetHostEntry(name).AddressList.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
                    Console.WriteLine($"Host Name: { Environment.MachineName} \t {name}\t {ip}");
                    await context.Response.WriteAsync($"Host Name: {Environment.MachineName}{Environment.NewLine}");
                    await context.Response.WriteAsync(Environment.NewLine);

                    // Request method, scheme, and path
                    await context.Response.WriteAsync($"Request Method: {context.Request.Method}{Environment.NewLine}");
                    await context.Response.WriteAsync($"Request Scheme: {context.Request.Scheme}{Environment.NewLine}");
                    await context.Response.WriteAsync($"Request URL: {context.Request.GetDisplayUrl()}{Environment.NewLine}");
                    await context.Response.WriteAsync($"Request Path: {context.Request.Path}{Environment.NewLine}");

                    // Headers
                    await context.Response.WriteAsync($"Request Headers:{Environment.NewLine}");
                    foreach (var (key, value) in context.Request.Headers)
                    {
                        await context.Response.WriteAsync($"\t {key}: {value}{Environment.NewLine}");
                    }
                    await context.Response.WriteAsync(Environment.NewLine);

                    // Connection: RemoteIp
                    await context.Response.WriteAsync($"Request Remote IP: {context.Connection.RemoteIpAddress}");
                });
            });
        }
    }
}
