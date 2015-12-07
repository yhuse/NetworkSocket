﻿using NetworkSocket.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace NetworkSocket.WebSocket
{
    /// <summary>
    /// 表示jsonWebSocket的Api异常上下文
    /// </summary>
    [DebuggerDisplay("Message = {Exception.Message}")]
    public class ExceptionContext : RequestContext, IExceptionContext
    {
        /// <summary>
        /// 获取异常对象
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        /// 获取或设置异常是否已处理
        /// 设置为true之后有异常不会抛出
        /// </summary>
        public bool ExceptionHandled { get; set; }

        /// <summary>
        /// 异常上下文
        /// </summary>
        /// <param name="context">请求上下文</param>
        /// <param name="exception">异常</param>
        public ExceptionContext(ActionContext context, Exception exception)
            : base(context.Session, context.Packet, context.AllSessions)
        {
            this.Exception = exception;
        }

        /// <summary>
        /// 异常上下文
        /// </summary>
        /// <param name="context">请求上下文</param>
        /// <param name="exception">异常</param>
        public ExceptionContext(RequestContext context, Exception exception)
            : base(context.Session, context.Packet, context.AllSessions)
        {
            this.Exception = exception;
        }

        /// <summary>
        /// 字符串显示
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return this.Exception.Message;
        }
    }
}
