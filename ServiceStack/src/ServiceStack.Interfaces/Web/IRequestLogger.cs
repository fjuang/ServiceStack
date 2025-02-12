﻿using System;
using System.Collections.Generic;

namespace ServiceStack.Web
{
    /// <summary>
    /// Log every service request
    /// </summary>
    public interface IRequestLogger
    {
        /// <summary>
        /// Turn On/Off Session Tracking
        /// </summary>
        bool EnableSessionTracking { get; set; }

        /// <summary>
        /// Turn On/Off Raw Request Body Tracking
        /// </summary>
        bool EnableRequestBodyTracking { get; set; }
        
        /// <summary>
        /// Turn On/Off Raw Request Body Tracking per-request
        /// </summary>
        Func<IRequest, bool> RequestBodyTrackingFilter { get; set; }

        /// <summary>
        /// Turn On/Off Tracking of Responses
        /// </summary>
        bool EnableResponseTracking { get; set; }

        /// <summary>
        /// Turn On/Off Tracking of Responses per-request
        /// </summary>
        Func<IRequest, bool> ResponseTrackingFilter { get; set; }

        /// <summary>
        /// Turn On/Off Tracking of Exceptions
        /// </summary>
        bool EnableErrorTracking { get; set; }

        /// <summary>
        /// Limit logging to only Service Requests
        /// </summary>
        bool LimitToServiceRequests { get; set; }

        /// <summary>
        /// Limit access to /requestlogs service to role
        /// </summary>
        string[] RequiredRoles { get; set; }

        /// <summary>
        /// Don't log matching requests
        /// </summary>
        Func<IRequest, bool> SkipLogging { get; set; }

        /// <summary>
        /// Don't log requests of these types.
        /// </summary>
        Type[] ExcludeRequestDtoTypes { get; set; }

        /// <summary>
        /// Don't log request bodys for services with sensitive information.
        /// By default Auth and Registration requests are hidden.
        /// </summary>
        Type[] HideRequestBodyForRequestDtoTypes { get; set; }
        
        /// <summary>
        /// Don't log Response DTO Types
        /// </summary>
        public Type[] ExcludeResponseTypes { get; set; }

        /// <summary>
        /// Customize Request Log Entry
        /// </summary>
        Action<IRequest, RequestLogEntry> RequestLogFilter { get; set; }

        /// <summary>
        /// Customize which instances should not be serialized
        /// </summary>
        Func<object,bool> IgnoreFilter { get; set; }
        
        /// <summary>
        /// Change what DateTime to use for the current Date (defaults to UtcNow)
        /// </summary>
        Func<DateTime> CurrentDateFn { get; set; }

        /// <summary>
        /// Log a request
        /// </summary>
        /// <param name="request">The RequestContext</param>
        /// <param name="requestDto">Request DTO</param>
        /// <param name="response">Response DTO or Exception</param>
        /// <param name="elapsed">How long did the Request take</param>
        void Log(IRequest request, object requestDto, object response, TimeSpan elapsed);

        /// <summary>
        /// View the most recent logs
        /// </summary>
        /// <param name="take"></param>
        /// <returns></returns>
        List<RequestLogEntry> GetLatestLogs(int? take);
    }
}