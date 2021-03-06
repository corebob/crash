﻿/*	
	Gamma Analyzer - Controlling application for Burn
    Copyright (C) 2016  Norwegian Radiation Protection Authority

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
// Authors: Dag robole,

using System;
using System.Net;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using Newtonsoft.Json;
using log4net;

namespace burn
{        
    /**
     * NetService - Threaded class for network communication
     */
    public partial class NetService
    {
        private ILog log = null;
        //! Running state for this service
        private volatile bool running;

        //! UDP configuration
        private const int servicePort = 9999;
        private const int recvTimeout = 100;
        private const int recvBufferSize = 1048576;

        //! Network utilities        
        private Socket socket = null;
        private EndPoint endPoint = null;

        //! Queue with messages from GUI client
        private ConcurrentQueue<ProtocolMessage> sendq = new ConcurrentQueue<ProtocolMessage>();

        //! Queue with messages from server
        private ConcurrentQueue<ProtocolMessage> recvq = new ConcurrentQueue<ProtocolMessage>();        

        /** 
         * Constructor for the NetService
         * \param sendQueue - Queue with messages from GUI client
         * \param recvQueue - Queue with messages from server
         */
        public NetService(ILog l, ref ConcurrentQueue<ProtocolMessage> sendQueue, ref ConcurrentQueue<ProtocolMessage> recvQueue)
        {            
            log = l;
            log.Info("Creating Net Service");
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), servicePort);
            running = true;
            sendQueue = sendq;
            recvQueue = recvq;        
        }

        /**
         * Thread entry point
         */
        public void DoWork()
        {
            try
            {
                log.Info("Initializing Net Service");
                var buffer = new byte[recvBufferSize]; // FIXME: configurable size
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, recvTimeout);

                while (running)
                {
                    // Send messages from analyzer
                    while (sendq.Count > 0)
                    {
                        ProtocolMessage sendMsg;
                        if (sendq.TryDequeue(out sendMsg))
                        {                            
                            IPEndPoint ep = new IPEndPoint(IPAddress.Parse(sendMsg.IPAddress), servicePort);
                            Byte[] sendBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(sendMsg.Params));
                            socket.SendTo(sendBytes, (EndPoint)ep);
                        }
                    }

                    // Receive messages from collector
                    while (socket.Available > 0)
                    {
                        int nbytes = socket.ReceiveFrom(buffer, ref endPoint);
                        if (nbytes > 0)
                        {
                            ProtocolMessage recvMsg = new ProtocolMessage(((IPEndPoint)endPoint).Address.ToString());
                            string jdata = Encoding.UTF8.GetString(buffer, 0, nbytes);                            
                            recvMsg.Params = JsonConvert.DeserializeObject<Dictionary<string, object>>(jdata);
                            recvq.Enqueue(recvMsg);
                        }
                    }

                    Thread.Sleep(50);
                }
            }
            catch (Exception ex)
            {
                log.Error(ex.Message, ex);
            }
        }

        /**
         * Function used to stop this service         
         */
        public void RequestStop()
        {
            running = false;
            log.Info("Stopping Net Service");
        }

        /**
         * Function used to check the running state of this service
         */
        public bool IsRunning()
        {
            return running;
        }
    }
}
