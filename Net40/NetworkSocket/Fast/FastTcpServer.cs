﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;

namespace NetworkSocket.Fast
{
    /// <summary>
    /// 快速构建Tcp服务端
    /// </summary>
    public abstract class FastTcpServer : TcpServerBase<FastSession>, IFastTcpServer
    {
        /// <summary>
        /// 所有Api行为
        /// </summary>
        private ApiActionList apiActionList;

        /// <summary>
        /// 数据包id提供者
        /// </summary>
        private PacketIdProvider packetIdProvider;

        /// <summary>
        /// 任务行为表
        /// </summary>
        private TaskSetActionTable taskSetActionTable;

        /// <summary>
        /// 获取或设置请求等待超时时间(毫秒)    
        /// 默认30秒
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public int TimeOut
        {
            get
            {
                return this.taskSetActionTable.TimeOut;
            }
            set
            {
                this.taskSetActionTable.TimeOut = value;
            }
        }

        /// <summary>
        /// 获取或设置序列化工具
        /// 默认是Json序列化
        /// </summary>
        public ISerializer Serializer { get; set; }

        /// <summary>
        /// 获取或设置Api行为特性过滤器提供者
        /// </summary>
        public IFilterAttributeProvider FilterAttributeProvider { get; set; }

        /// <summary>
        /// 快速构建Tcp服务端
        /// </summary>
        public FastTcpServer()
        {
            this.apiActionList = new ApiActionList();
            this.packetIdProvider = new PacketIdProvider();
            this.taskSetActionTable = new TaskSetActionTable();

            this.Serializer = new DefaultSerializer();
            this.FilterAttributeProvider = new FilterAttributeProvider();
        }

        /// <summary>
        /// 绑定本程序集所有实现IFastApiService的服务
        /// </summary>
        /// <returns></returns>       
        /// <exception cref="ArgumentException"></exception>
        public FastTcpServer BindService()
        {
            var allServices = this.GetType().Assembly.GetTypes().Where(item => typeof(IFastApiService).IsAssignableFrom(item));
            return this.BindService(allServices);
        }

        /// <summary>
        /// 绑定服务
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <returns></returns>       
        /// <exception cref="ArgumentException"></exception>
        public FastTcpServer BindService<T>() where T : IFastApiService
        {
            return this.BindService(typeof(T));
        }

        /// <summary>
        /// 绑定服务
        /// </summary>
        /// <param name="serviceType">服务类型</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public FastTcpServer BindService(params Type[] serviceType)
        {
            return this.BindService((IEnumerable<Type>)serviceType);
        }

        /// <summary>
        /// 绑定服务
        /// </summary>
        /// <param name="serivceType">服务类型</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public FastTcpServer BindService(IEnumerable<Type> serivceType)
        {
            if (serivceType == null)
            {
                throw new ArgumentNullException("serivceType");
            }

            if (serivceType.Any(item => item == null))
            {
                throw new ArgumentException("serivceType不能含null值");
            }

            if (serivceType.Any(item => typeof(IFastApiService).IsAssignableFrom(item) == false))
            {
                throw new ArgumentException("serivceType必须派生于IFastApiService");
            }

            foreach (var type in serivceType)
            {
                var actions = FastTcpCommon.GetServiceApiActions(type);
                this.apiActionList.AddRange(actions);
            }
            return this;
        }

        /// <summary>
        /// 创建新的会话对象
        /// </summary>
        /// <returns></returns>
        protected override FastSession OnCreateSession()
        {
            return new FastSession(this.packetIdProvider, this.taskSetActionTable, this.Serializer, this.FilterAttributeProvider);
        }

        /// <summary>
        /// 当接收到会话对象的数据时，将触发此方法  
        /// </summary>
        /// <param name="session">会话对象</param>
        /// <param name="builder">接收到的历史数据</param>
        /// <returns></returns>
        protected override void OnReceive(FastSession session, ByteBuilder builder)
        {
            FastPacket packet;
            while ((packet = FastPacket.From(builder)) != null)
            {
                this.OnRecvComplete(session, packet);
            }
        }

        /// <summary>
        /// 当接收到会话对象的数据包时
        /// </summary>
        /// <param name="session">会话对象</param>
        /// <param name="packet">接收到的数据类型</param>
        private void OnRecvComplete(FastSession session, FastPacket packet)
        {
            var requestContext = new RequestContext { Session = session, Packet = packet, AllSessions = this.AllSessions };

            if (packet.IsException)
            {
                this.ProcessRemoteException(requestContext);
            }
            else
            {
                this.ProcessRequest(requestContext);
            }
        }

        /// <summary>
        /// 处理远返回的程异常
        /// </summary>
        /// <param name="requestContext">请求上下文</param>
        private void ProcessRemoteException(RequestContext requestContext)
        {
            var remoteException = FastTcpCommon.SetApiActionTaskException(this.Serializer, this.taskSetActionTable, requestContext);
            if (remoteException == null)
            {
                return;
            }
            var exceptionContext = new ExceptionContext(requestContext, remoteException);
            this.ExecExceptionFilters(exceptionContext);

            if (exceptionContext.ExceptionHandled == false)
            {
                throw exceptionContext.Exception;
            }
        }

        /// <summary>
        /// 处理正常的数据请求
        /// </summary>
        /// <param name="requestContext">请求上下文</param>       
        private void ProcessRequest(RequestContext requestContext)
        {
            if (requestContext.Packet.IsFromClient == false)
            {
                FastTcpCommon.SetApiActionTaskResult(requestContext, this.taskSetActionTable);
                return;
            }

            var action = this.GetApiAction(requestContext);
            if (action == null)
            {
                return;
            }

            var actionContext = new ActionContext(requestContext, action);
            var fastApiService = this.GetFastApiService(actionContext);
            if (fastApiService == null)
            {
                return;
            }

            // 执行Api行为           
            fastApiService.Execute(actionContext);
            // 释放资源
            DependencyResolver.Current.TerminateService(fastApiService);
        }

        /// <summary>
        /// 获取Api行为
        /// </summary>
        /// <param name="requestContext">请求上下文</param>
        /// <returns></returns>
        private ApiAction GetApiAction(RequestContext requestContext)
        {
            var action = this.apiActionList.TryGet(requestContext.Packet.ApiName);
            if (action != null)
            {
                return action;
            }

            var exception = new ApiNotExistException(requestContext.Packet.ApiName);
            var exceptionContext = new ExceptionContext(requestContext, exception);

            FastTcpCommon.SetRemoteException(requestContext.Session, requestContext.Session.Serializer, exceptionContext);
            this.ExecExceptionFilters(exceptionContext);

            if (exceptionContext.ExceptionHandled == false)
            {
                throw exception;
            }

            return null;
        }

        /// <summary>
        /// 获取FastApiService实例
        /// </summary>
        /// <param name="actionContext">Api行为上下文</param>
        /// <returns></returns>
        private IFastApiService GetFastApiService(ActionContext actionContext)
        {
            // 获取服务实例
            var fastApiService = (IFastApiService)DependencyResolver.Current.GetService(actionContext.Action.DeclaringService);
            if (fastApiService != null)
            {
                return fastApiService;
            }
            var exception = new Exception(string.Format("无法获取类型{0}的实例", actionContext.Action.DeclaringService));
            var exceptionContext = new ExceptionContext(actionContext, exception);

            FastTcpCommon.SetRemoteException(actionContext.Session, actionContext.Session.Serializer, exceptionContext);
            this.ExecExceptionFilters(exceptionContext);

            if (exceptionContext.ExceptionHandled == false)
            {
                throw exception;
            }

            return null;
        }


        /// <summary>
        /// 执行异常过滤器
        /// </summary>         
        /// <param name="exceptionContext">上下文</param>       
        private void ExecExceptionFilters(ExceptionContext exceptionContext)
        {
            foreach (var filter in GlobalFilters.ExceptionFilters)
            {
                if (exceptionContext.ExceptionHandled == false)
                {
                    filter.OnException(exceptionContext);
                }
            }
        }

        #region IDisponse
        /// <summary>
        /// 释放资源
        /// </summary>
        /// <param name="disposing">是否也释放托管资源</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                this.apiActionList = null;

                this.taskSetActionTable.Clear();
                this.taskSetActionTable = null;

                this.packetIdProvider = null;
                this.Serializer = null;
                this.FilterAttributeProvider = null;
            }
        }
        #endregion
    }
}