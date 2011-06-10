﻿// Copyright (c) 2010 - OJ Reeves & Jeremiah Peschka
//
// This file is provided to you under the Apache License,
// Version 2.0 (the "License"); you may not use this file
// except in compliance with the License.  You may obtain
// a copy of the License at
//
//   http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using CorrugatedIron.Config;
using CorrugatedIron.Encoding;
using CorrugatedIron.Extensions;
using CorrugatedIron.Messages;
using CorrugatedIron.Models.Rest;
using CorrugatedIron.Util;

namespace CorrugatedIron.Comms
{
    public interface IRiakConnection : IDisposable
    {
        bool IsIdle { get; }
        void BeginIdle();
        void EndIdle();

        // PBC interface
        RiakResult<TResult> PbcRead<TResult>()
            where TResult : new();
        RiakResult PbcWrite<TRequest>(TRequest request);
        RiakResult<TResult> PbcWriteRead<TRequest, TResult>(TRequest request)
            where TResult : new();

        // REST interface
        RiakResult<RiakRestResponse> RestRequest(RiakRestRequest request);
        RiakResult SetClientId(byte[] clientId);
    }

    public class RiakConnection : IRiakConnection
    {
        private readonly IRiakNodeConfiguration _nodeConfiguration;
        private readonly MessageEncoder _encoder;
        private readonly object _idleTimerLock = new object();
        private readonly string _restRootUrl;
        private readonly RiakPbcClientSocket _socket;
        private Timer _idleTimer;
        private string _restClientId;
        public int RefCount;
        public volatile bool Read = true;

        public bool IsIdle
        {
            get { return !_socket.IsConnected; }
        }

        static RiakConnection()
        {
            ServicePointManager.ServerCertificateValidationCallback += ServerValidationCallback;
        }

        public RiakConnection(IRiakNodeConfiguration nodeConfiguration)
        {
            _nodeConfiguration = nodeConfiguration;
            _restRootUrl = @"{0}://{1}:{2}".Fmt(nodeConfiguration.RestScheme, nodeConfiguration.HostAddress, nodeConfiguration.RestPort);
            _encoder = new MessageEncoder();
            _socket = new RiakPbcClientSocket(nodeConfiguration.HostAddress, nodeConfiguration.PbcPort, nodeConfiguration.AcquireTimeout);
        }

        public static byte[] ToClientId(int id)
        {
            return BitConverter.GetBytes(id);
        }

        public void BeginIdle()
        {
            if (IsIdle) return;
            if (_idleTimer != null) return;

            lock (_idleTimerLock)
            {
                if (IsIdle) return;
                if (_idleTimer != null) return;

                _idleTimer = new Timer(_ => GoIdle(), null, 0, _nodeConfiguration.IdleTimeout);
            }
        }

        public void EndIdle()
        {
            CleanUpTimer();
            _socket.Connect();
        }

        public RiakResult SetClientId(byte[] clientId)
        {
            var result = PbcWriteRead<RpbSetClientIdReq, RpbSetClientIdResp>(new RpbSetClientIdReq { ClientId = clientId });

            if (result.IsSuccess)
            {
                _restClientId = Convert.ToBase64String(clientId);
            }
            return result;
        }

        public RiakResult<TResult> PbcRead<TResult>()
            where TResult : new()
        {
            try
            {
                if (Read)
                {
                    System.Diagnostics.Debugger.Break();
                }
                var data = _socket.Receive();
                var result = _encoder.Decode<TResult>(data);
                Read = true;
                return RiakResult<TResult>.Success(result);
            }
            catch (SocketException ex)
            {
                return RiakResult<TResult>.Error(ResultCode.CommunicationError, ex.Message);
            }
        }

        public RiakResult PbcWrite<TRequest>(TRequest request)
        {
            try
            {
                if (!Read)
                {
                    System.Diagnostics.Debugger.Break();
                }
                var data = _encoder.Encode(request);
                _socket.Send(data);
                Read = false;
                return RiakResult.Success();
            }
            catch (SocketException ex)
            {
                return RiakResult.Error(ResultCode.CommunicationError, ex.Message);
            }
        }

        public RiakResult<TResult> PbcWriteRead<TRequest, TResult>(TRequest request)
            where TResult : new()
        {
            var writeResult = PbcWrite(request);
            if (writeResult.IsSuccess)
            {
                return PbcRead<TResult>();
            }
            return RiakResult<TResult>.Error(writeResult.ResultCode, writeResult.ErrorMessage);
        }

        public RiakResult<RiakRestResponse> RestRequest(RiakRestRequest request)
        {
            var baseUri = new StringBuilder(_restRootUrl).Append(request.Uri);
            if (request.QueryParams.Count > 0)
            {
                baseUri.Append("?");
                var first = request.QueryParams.First();
                baseUri.Append(first.Key.UrlEncoded()).Append("=").Append(first.Value.UrlEncoded());
                request.QueryParams.Skip(1).ForEach(kv => baseUri.Append("&").Append(kv.Key.UrlEncoded()).Append("=").Append(kv.Value.UrlEncoded()));
            }
            var targetUri = new Uri(baseUri.ToString());

            var req = (HttpWebRequest)WebRequest.Create(targetUri);
            req.KeepAlive = true;
            req.Method = request.Method;
            req.Credentials = CredentialCache.DefaultCredentials;

            if (!string.IsNullOrWhiteSpace(request.ContentType))
            {
                req.ContentType = request.ContentType;
            }

            if (!request.Cache)
            {
                req.Headers.Set(Constants.Rest.HttpHeaders.DisableCacheKey, Constants.Rest.HttpHeaders.DisableCacheValue);
            }

            request.Headers.Add(Constants.Rest.HttpHeaders.ClientId, _restClientId);

            request.Headers.ForEach(h => req.Headers.Set(h.Key, h.Value));

            if (request.Body != null && request.Body.Length > 0)
            {
                req.ContentLength = request.Body.Length;
                using (var writer = req.GetRequestStream())
                {
                    writer.Write(request.Body, 0, request.Body.Length);
                }
            }
            else
            {
                req.ContentLength = 0;
            }

            try
            {
                var response = (HttpWebResponse)req.GetResponse();

                var result = new RiakRestResponse
                {
                    ContentLength = response.ContentLength,
                    ContentType = response.ContentType,
                    StatusCode = response.StatusCode,
                    Headers = response.Headers.AllKeys.ToDictionary(k => k, k => response.Headers[k]),
                    ContentEncoding = !string.IsNullOrWhiteSpace(response.ContentEncoding)
                                          ? System.Text.Encoding.GetEncoding(response.ContentEncoding)
                                          : System.Text.Encoding.Default
                };

                if (response.ContentLength > 0)
                {
                    using (var responseStream = response.GetResponseStream())
                    using (var reader = new StreamReader(responseStream, result.ContentEncoding))
                    {
                        result.Body = reader.ReadToEnd();
                    }
                }

                return RiakResult<RiakRestResponse>.Success(result);
            }
            catch (Exception ex)
            {
                return RiakResult<RiakRestResponse>.Error(ResultCode.CommunicationError, ex.Message);
            }
        }

        private static bool ServerValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        public void Dispose()
        {
            CleanUp();
            _socket.Dispose();
        }

        private void GoIdle()
        {
            //CleanUp();
        }

        private void CleanUp()
        {
            _socket.Disconnect();
            CleanUpTimer();
        }

        private void CleanUpTimer()
        {
            if (_idleTimer == null) return;

            lock (_idleTimerLock)
            {
                // ignore R#'s warning, this IS possible across threads.
                if (_idleTimer == null) return;
                _idleTimer.Dispose();
                _idleTimer = null;
            }
        }
    }
}
