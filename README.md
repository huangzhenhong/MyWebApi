## WebApi vs Nginx Load Balancer

### Create an ASP.NET Core Web API Project

Run following command to create the webapi project
```
Zhenhongs-MacBook-Pro:projects zhenhong$ dotnet new webapi -o MyWebApi --no-restore
The template "ASP.NET Core Web API" was created successfully.
Zhenhongs-MacBook-Pro:projects zhenhong$ cd MyWebApi/
Zhenhongs-MacBook-Pro:MyWebApi zhenhong$ ls
Controllers			Program.cs			Startup.cs			appsettings.Development.json
MyWebApi.csproj			Properties			WeatherForecast.cs		appsettings.json
Zhenhongs-MacBook-Pro:MyWebApi zhenhong$ code .
```

We first create an ASP.NET Core Web API project MyWebApi from the dotnet template. Since we will use Nginx for SSL termination (more in next section), we don’t need to enable HTTPS support in our ASP.NET Core project.

The MyWebApi app will be hosted by Kestrel which is sitting behind Nginx. HTTPS requests are proxied over HTTP, so the original scheme (HTTPS) and originating client IP address are lost. To remediate that, we can forward the original scheme and client IP using forwarded headers by enabling the Forwarded Headers Middleware. Note that the Forwarded Headers Middleware should run before other middlewares. The following code snippet shows an example implementation.

Startup.cs file
```
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

```
Run the application to check if it is working or not 
```
Zhenhongs-MacBook-Pro:MyWebApi zhenhong$ dotnet run 
info: Microsoft.Hosting.Lifetime[0]
      Now listening on: https://localhost:5001
info: Microsoft.Hosting.Lifetime[0]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Development
info: Microsoft.Hosting.Lifetime[0]
      Content root path: /Users/zhenhong/projects/MyWebApi
Host Name: Zhenhongs-MacBook-Pro         Zhenhongs-MacBook-Pro.local     127.0.0.1
```
Go to browser https://localhost:5001/, I got this information which means my app is working
```
Host Name: Zhenhongs-MacBook-Pro

Request Method: GET
Request Scheme: https
Request URL: https://localhost:5001/
Request Path: /
Request Headers:
	 Connection: keep-alive
	 Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9
	 Accept-Encoding: gzip, deflate, br
	 Accept-Language: en-US,en;q=0.9,zh-CN;q=0.8,zh;q=0.7
	 Cookie: SESS49960de5880e8c687434170f6476605b=z84ZLL78XmxpKdt7t6GaGMaxf4SciIyQD4d0wc9mppM
	 Host: localhost:5001
	 User-Agent: Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_6) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/85.0.4183.121 Safari/537.36
	 Upgrade-Insecure-Requests: 1
	 Sec-Fetch-Site: none
	 Sec-Fetch-Mode: navigate
	 Sec-Fetch-User: ?1
	 Sec-Fetch-Dest: document

Request Remote IP: ::1
```

#### Build and Run app in Docker container

We will build and run the MyWebApi app in a Docker container, so a Dockerfile and a .dockerignore file are needed. The following code snippet shows an example Dockerfile for containerizing an ASP.NET Core project.
```
FROM mcr.microsoft.com/dotnet/core/sdk:3.1-alpine AS build
WORKDIR /app

COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-alpine AS runtime
WORKDIR /app
COPY --from=build /out ./
ENV ASPNETCORE_URLS http://*:5000
ENTRYPOINT ["dotnet", "MyWebApi.dll"]
```
You can check the docker doc https://docs.docker.com/engine/examples/dotnetcore/ for more information. You need to update the dll name to yours. 

To make your build context as small as possible add a .dockerignore file to your project folder and copy the following into it.
```
# directories
**/bin/
**/obj/
**/out/
**/.git/
**/node_modules/

# files
Dockerfile*
**/*.md
```

#### *Optional: you can build your docker image from here*
```
Zhenhongs-MacBook-Pro:MyWebApi zhenhong$ docker image build -t dockerglue/dotnetcorewebapi31 .
[+] Building 0.9s (15/15) FINISHED                                                                                                                                       
 => [internal] load build definition from Dockerfile                                                                                                                0.0s
 => => transferring dockerfile: 390B                                                                                                                                0.0s
 => [internal] load .dockerignore                                                                                                                                   0.0s
 => => transferring context: 34B                                                                                                                                    0.0s
 => [internal] load metadata for mcr.microsoft.com/dotnet/core/aspnet:3.1-alpine                                                                                    0.2s
 => [internal] load metadata for mcr.microsoft.com/dotnet/core/sdk:3.1-alpine                                                                                       0.3s
 => [internal] load build context                                                                                                                                   0.0s
 => => transferring context: 661B                                                                                                                                   0.0s
 => [runtime 1/3] FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-alpine@sha256:925aee3359b42911c3604e2a37f432267d764e8ca283161681134df3b6c40d51                      0.0s
 => [build 1/6] FROM mcr.microsoft.com/dotnet/core/sdk:3.1-alpine@sha256:72ca50f170bde28915273997538ce9d26fc38e1fce6563a2bfdf3c339778e034                           0.0s
 => CACHED [runtime 2/3] WORKDIR /app                                                                                                                               0.0s
 => CACHED [build 2/6] WORKDIR /app                                                                                                                                 0.0s
 => CACHED [build 3/6] COPY *.csproj ./                                                                                                                             0.0s
 => CACHED [build 4/6] RUN dotnet restore                                                                                                                           0.0s
 => CACHED [build 5/6] COPY . ./                                                                                                                                    0.0s
 => CACHED [build 6/6] RUN dotnet publish -c Release -o /out --no-restore                                                                                           0.0s
 => [runtime 3/3] COPY --from=build /out ./                                                                                                                         0.1s
 => exporting to image                                                                                                                                              0.0s
 => => exporting layers                                                                                                                                             0.0s
 => => writing image sha256:e7f66c42c59edd8c1a7363391530bede69f279f3949728c6e139a7610e50ff55                                                                        0.0s
 => => naming to docker.io/dockerglue/dotnetcorewebapi31                                                                                                            0.0s

Zhenhongs-MacBook-Pro:MyWebApi zhenhong$ docker image ls 
REPOSITORY                                                       TAG                                              IMAGE ID            CREATED             SIZE
dockerglue/dotnetcorewebapi31                                    latest                                           e7f66c42c59e        15 seconds ago      105MB

```
It will take some minutes to build the image, it depends on your network.

### Load Balancing using Nginx
We will create two configuration files, nginx.conf and proxy.conf, to set up the Nginx server.

I followed the article, [Host ASP.NET Core on Linux with Nginx](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-3.1#configure-nginx), to create a proxy.conf file. The content is as follows, which is pretty standard.

```
proxy_redirect          off;
proxy_http_version      1.1;
proxy_set_header        Upgrade             $http_upgrade;
proxy_cache_bypass      $http_upgrade;
proxy_set_header        Connection          keep-alive;
proxy_set_header        Host $host;
proxy_set_header        X-Real-IP           $remote_addr;
proxy_set_header        X-Forwarded-For     $proxy_add_x_forwarded_for;
proxy_set_header        X-Forwarded-Proto   $scheme;
proxy_set_header        X-Forwarded-Host    $server_name;
client_max_body_size    10m;
client_body_buffer_size 128k;
proxy_connect_timeout   90;
proxy_send_timeout      90;
proxy_read_timeout      90;
proxy_buffers           32 4k;
```

The nginx.conf file has the following content.

```
user nginx;

worker_processes    auto;

events { worker_connections 1024; }

http {
    log_format upstream_time '$remote_addr $http_x_forwarded_for - $remote_user [$time_local] '
                             '$ssl_protocol "$request" $status $body_bytes_sent '
                             '"$http_referer" "$http_user_agent"'
                             'rt=$request_time uct="$upstream_connect_time" uht="$upstream_header_time" urt="$upstream_response_time"';
    access_log /var/log/nginx/access.log upstream_time;

    # Include the proxy.conf file and mime.types file
    include         /etc/nginx/proxy.conf;
    include         /etc/nginx/mime.types;
    limit_req_zone  $binary_remote_addr zone=one:10m rate=5r/s;
    server_tokens   off;

    sendfile on;
    keepalive_timeout   29; # Adjust to the lowest possible value that makes sense for your use case.
    client_body_timeout 10; client_header_timeout 10; send_timeout 10;

    upstream webapi {
        server api:5000;
    }

    server {
        listen                      80;
        server_name                 $hostname;
        
        location / {
            proxy_pass http://webapi;
            limit_req  zone=one burst=10 nodelay;
        }
    }
}
```

The load balancing is achieved by the upstream directive and the proxy_pass directive. When a request hits the location “/”, the Nginx server finds the upstream “webapi”, then proxies the request to another server api:5000. The upstream webapi uses the default round-robin approach to load balance the requests, such that requests are evenly distributed across all available replicate servers.

The server host name api is determined by the service name in Docker Compose. The following code snippet shows an example docker-compose.yml file.
```
version: '3'
services:
  nginx:
    image: nginx:alpine
    hostname: 'nginx'
    volumes:
      - ./nginx/nginx.conf:/etc/nginx/nginx.conf:ro
      - ./nginx/proxy.conf:/etc/nginx/proxy.conf:ro
      - ./nginx/logs/:/var/log/nginx/
    ports:
      - '80:80'
    depends_on:
      - api
    restart: always

  api:
    build:
        context: .
        dockerfile: api.Dockerfile
    image: dockerglue/dotnetcorewebapi31
    ports:
      - '5000'
    restart: always
```
With the docker-compose.yml file, we can launch the nginx and api services using the following command.
```
Zhenhongs-MacBook-Pro:MyWebApi zhenhong$ docker-compose up --scale api=4 --build
WARNING: The Docker Engine you're using is running in swarm mode.

Compose does not use swarm mode to deploy services to multiple nodes in a swarm. All containers will be scheduled on the current node.

To deploy your application across the swarm, use `docker stack deploy`.

Building api
Step 1/11 : FROM mcr.microsoft.com/dotnet/core/sdk:3.1-alpine AS build
 ---> d76f75ea19ff
Step 2/11 : WORKDIR /app
 ---> Using cache
 ---> 7c535b5fe7b6
Step 3/11 : COPY *.csproj ./
 ---> Using cache
 ---> 212b1582d604
Step 4/11 : RUN dotnet restore
 ---> Using cache
 ---> 61ae51d65fd4
Step 5/11 : COPY . ./
 ---> 5526a23c40bf
Step 6/11 : RUN dotnet publish -c Release -o /out --no-restore
 ---> Running in 1dc1603ab405
Microsoft (R) Build Engine version 16.7.0+7fb82e5b2 for .NET
Copyright (C) Microsoft Corporation. All rights reserved.

```

When all containers are running, we can visit the address http://localhost multiple times. By checking the console logs and web page content, we can observe that the requests are processed by the four api services in turn.

### Reference

https://codeburst.io/load-balancing-an-asp-net-core-web-app-using-nginx-and-docker-66753eb08204
