﻿using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Funq;
using NUnit.Framework;
using ServiceStack.Text;
using ServiceStack.Web;

namespace ServiceStack.WebHost.Endpoints.Tests
{
    [TestFixture]
    public class ExceptionHandlingTestsAsync
    {
        readonly AppHost appHost;
        public ExceptionHandlingTestsAsync()
        {
            appHost = new AppHost();
            appHost.Init();
            appHost.Start(Config.ListeningOn);
        }

        [OneTimeTearDown]
        public void OnTestFixtureTearDown()
        {
            appHost.Dispose();
            appHost.UncaughtExceptionHandlers = null;
        }

        public class AppHost : AppSelfHostBase
        {
            public AppHost()
                : base(nameof(ExceptionHandlingTestsAsync), typeof(UserService).Assembly) { }

            public static int OnEndRequestCallbacksCount;

            public override void Configure(Container container)
            {
                SetConfig(new HostConfig { DebugMode = false });
                
                OnEndRequestCallbacks.Add(req => {
                    Interlocked.Increment(ref OnEndRequestCallbacksCount);
                });
                
                GlobalRequestFilters.Add((req, res, dto) => {
                    if (dto is UncatchedException || dto is UncatchedExceptionAsync)
                        throw new ArgumentException();
                });

                //Custom global uncaught exception handling strategy
                this.UncaughtExceptionHandlersAsync.Add(async (req, res, operationName, ex) =>
                {
                    await res.WriteAsync($"UncaughtException {ex.GetType().Name}");
                    res.EndRequest(skipHeaders: true);
                });

                this.ServiceExceptionHandlersAsync.Add(async (httpReq, request, ex) =>
                {
                    await Task.Yield();

                    if (request is UncatchedException || request is UncatchedExceptionAsync)
                        throw ex;

                    if (request is CaughtException || request is CaughtExceptionAsync)
                        return DtoUtils.CreateErrorResponse(request, new ArgumentException("ExceptionCaught"));

                    return null;
                });
            }

            public override Task OnUncaughtException(IRequest httpReq, IResponse httpRes, string operationName, Exception ex)
            {
                "In OnUncaughtException...".Print();
                return base.OnUncaughtException(httpReq, httpRes, operationName, ex);
            }
        }

        public string PredefinedJsonUrl<T>()
        {
            return Config.ListeningOn + "json/reply/" + typeof(T).Name;
        }
        
        [Test]
        public void Can_override_global_exception_handling()
        {
#pragma warning disable CS0618, SYSLIB0014
            var req = WebRequest.CreateHttp(PredefinedJsonUrl<UncatchedException>());
#pragma warning restore CS0618, SYSLIB0014
            var res = req.GetResponse().ReadToEnd();
            Assert.AreEqual("UncaughtException ArgumentException", res);
        }

        [Test]
        public void Can_override_global_exception_handling_async()
        {
#pragma warning disable CS0618, SYSLIB0014
            var req = WebRequest.CreateHttp(PredefinedJsonUrl<UncatchedExceptionAsync>());
#pragma warning restore CS0618, SYSLIB0014
            var res = req.GetResponse().ReadToEnd();
            Assert.AreEqual("UncaughtException ArgumentException", res);
        }

        [Test]
        public void Can_override_caught_exception()
        {
            try
            {
#pragma warning disable CS0618, SYSLIB0014
                var req = WebRequest.CreateHttp(PredefinedJsonUrl<CaughtException>());
#pragma warning restore CS0618, SYSLIB0014
                var res = req.GetResponse().ReadToEnd();
                Assert.Fail("Should Throw");
            }
            catch (WebException ex)
            {
                Assert.That(ex.IsAny400());
                var json = ex.GetResponseBody();
                var response = json.FromJson<ErrorResponse>();
                Assert.That(response.ResponseStatus.Message, Is.EqualTo("ExceptionCaught"));
            }
        }

        [Test]
        public void Can_override_caught_exception_async()
        {
            try
            {
#pragma warning disable CS0618, SYSLIB0014
                var req = WebRequest.CreateHttp(PredefinedJsonUrl<CaughtExceptionAsync>());
#pragma warning restore CS0618, SYSLIB0014
                var res = req.GetResponse().ReadToEnd();
                Assert.Fail("Should Throw");
            }
            catch (WebException ex)
            {
                Assert.That(ex.IsAny400());
                var json = ex.GetResponseBody();
                var response = json.FromJson<ErrorResponse>();
                Assert.That(response.ResponseStatus.Message, Is.EqualTo("ExceptionCaught"));
            }
        }

        [Test]
        public void Request_binding_error_raises_UncaughtException()
        {
            Interlocked.Exchange(ref AppHost.OnEndRequestCallbacksCount, 0);
            
            var response = PredefinedJsonUrl<ExceptionWithRequestBinding>()
                .AddQueryParam("Id", "NaN")
                .GetStringFromUrl();

            Assert.That(AppHost.OnEndRequestCallbacksCount, Is.EqualTo(1));
            Assert.That(response, Is.EqualTo("UncaughtException SerializationException"));
        }
    }
}