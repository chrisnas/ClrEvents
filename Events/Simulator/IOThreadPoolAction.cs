using System;
using System.IO;
using System.Net;
using System.Threading;

namespace Simulator
{
    public static class IOThreadPoolAction
    {
        public static void Run()
        {
            // https://www.youtube.com/watch?v=5Om7NbfUC6Y
            // 5 minutes moon phases video
            var url = "https://www.youtube.com/watch?v=5Om7NbfUC6Y";
            HttpWebRequest request = WebRequest.CreateHttp(url);
            var state = new RequestState();
            state.Request = request;
            IAsyncResult ar = request.BeginGetResponse(ResponseCallBack, state);
        }

        private static void ResponseCallBack(IAsyncResult ar)
        {
            var state = ar.AsyncState as RequestState;
            var response = state.Request.EndGetResponse(ar);
            state.ResponseStream = response.GetResponseStream();

            Thread.Sleep(2000000);
            state.ResponseStream.BeginRead(state.ReadBuffer, 0, RequestState.BufferSize, ReadCallback, state);
        }

        private static void ReadCallback(IAsyncResult ar)
        {
            var state = ar.AsyncState as RequestState;

            int read = state.ResponseStream.EndRead(ar);
            if (read > 0)
            {
                // we don't care about the content: just release scheduling quantum before reading the next buffer
                Thread.Yield();

                state.ResponseStream.BeginRead(state.ReadBuffer, 0, RequestState.BufferSize, ReadCallback, state);
            }
            else
            {
                state.ResponseStream.Close();
            }
        }
    }

    class RequestState
    {
        public const int BufferSize = 100*1024;
        public byte[] ReadBuffer = new byte[BufferSize];
        public HttpWebRequest Request;
        public Stream ResponseStream;
    }
}
