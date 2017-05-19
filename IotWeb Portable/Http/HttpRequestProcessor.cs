using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IotWeb.Common.Util;
using Newtonsoft.Json;

namespace IotWeb.Common.Http
{
    class HttpRequestProcessor
    {
        // Regular expression for parsing the start line
        private static Regex RequestStartLine = new Regex(@"^([a-zA-z]+)[ ]+([^ ]+)[ ]+[hH][tT][tT][pP]/([0-9]\.[0-9])$");

        // Regular expression for parsing headers
        private static Regex HeaderLine = new Regex(@"^([a-zA-Z][a-zA-Z0-9\-]*):(.*)");

        // Cookie separators
        private static char[] CookieSeparator = new char[] { ';' };
        private static char[] CookieValueSeparator = new char[] { '=' };

        // WebSocket protocol separator
        private static char[] WebSocketProtocolSeparator = new char[] { ',' };

        private string _boundaryDelimeter = "";
        private Stream _inputStream;
        private Element _currentElement;
        private byte[] _tempData;
        private int _tempDataCurrentIndex = 0;
        private int _tempDataIndexBeforeBodyReading = -1;
        private bool _endOfStream = false;
        private int _dataRead = 0;
        private int _startIndex = 0;
        private DecodedData decodedData;
        private int _contentLength;

        // States for the request parser
        private enum RequestParseState
        {
            StartLine,
            Headers,
            Body
        }

        // Constants
        private const int MaxRequestBody = 101000000; //~101MB
        private const int InputBufferSize = 1024;
        private const byte CR = 0x0d;
        private const byte LF = 0x0a;

        // WebSocket header fields
        private static string SecWebSocketKey = "Sec-WebSocket-Key";
        private static string SecWebSocketProtocol = "Sec-WebSocket-Protocol";
        private static string SecWebSocketVersion = "Sec-WebSocket-Version";
        private static string SecWebSocketAccept = "Sec-WebSocket-Accept";

        // Instance variables
        private byte[] m_buffer;
        private int m_index;
        private bool m_connected;
        private string m_lastHeader;
        private BaseHttpServer m_server;

        /// <summary>
        /// Default constructor
        /// </summary>
        public HttpRequestProcessor(BaseHttpServer server)
        {
            m_buffer = new byte[InputBufferSize];
            m_index = 0;
            m_connected = true;
            m_lastHeader = null;
            m_server = server;
        }

        /// <summary>
        /// Handle the HTTP connection
        /// 
        /// This implementation doesn't support keep alive so each HTTP session
        /// consists of parsing the request, dispatching to a handler and then
        /// sending the response before closing the connection.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="input"></param>
        /// <param name="output"></param>
        public void ProcessHttpRequest(Stream input, Stream output)
        {
            // Set up state
            HttpRequest request = null;
            HttpResponse response = null;
            HttpException parseError = null;
            HttpContext context = null;
            // Process the request
            try
            {
                request = ParseRequest(input);
                _tempData = null;

                if ((request == null) || !m_connected)
                    return; // Nothing we can do, just drop the connection
                // Do we have any content in the body ?
                if (request.Headers.ContainsKey(HttpHeaders.ContentType))
                {
                    if (!request.Headers.ContainsKey(HttpHeaders.ContentLength))
                        throw new HttpLengthRequiredException();
                    int length;
                    if (!int.TryParse(request.Headers[HttpHeaders.ContentLength], out length))
                        throw new HttpLengthRequiredException();
                    request.ContentLength = length;
                    if (length > MaxRequestBody)
                        throw new HttpRequestEntityTooLargeException();

                    if (!_endOfStream)
                    {
                        UpdateConentBody(request);
                    }
                }
                // Process the cookies
                if (request.Headers.ContainsKey(HttpHeaders.Cookie))
                {
                    string[] cookies = request.Headers[HttpHeaders.Cookie].Split(CookieSeparator);
                    foreach (string cookie in cookies)
                    {
                        string[] parts = cookie.Split(CookieValueSeparator);
                        Cookie c = new Cookie();
                        c.Name = parts[0].Trim();
                        if (parts.Length > 1)
                            c.Value = parts[1].Trim();
                        request.Cookies.Add(c);
                    }
                }
                // We have at least a partial request, create the matching response
                context = new HttpContext();
                response = new HttpResponse();
                // Apply filters
                if (m_server.ApplyBeforeFilters(request, response, context))
                {
                    // Check for WebSocket upgrade
                    IWebSocketRequestHandler wsHandler = UpgradeToWebsocket(request, response);
                    if (wsHandler != null)
                    {
                        // Apply the after filters here
                        m_server.ApplyAfterFilters(request, response, context);
                        // Write the response back to accept the connection
                        /////////////////////////////////Changes done locally to fix HTTP 1.1 on Safari 10 websocket error on 22.11.2016/////////////////////
                        response.Send(output, HttpVersion.Ver1_1);
                        output.Flush();
                        // Now we can process the websocket
                        WebSocket ws = new WebSocket(input, output);
                        wsHandler.Connected(ws);
                        ws.Run();
                        // Once the websocket connection is finished we don't need to do anything else
                        return;
                    }
                    // Dispatch to the handler
                    string partialUri;
                    IHttpRequestHandler handler = m_server.GetHandlerForUri(request.URI, out partialUri);
                    if (handler == null)
                        throw new HttpNotFoundException();
                    
                    request.DecodedData = decodedData;
                    handler.HandleRequest(partialUri, request, response, context);
                }
            }
            catch (HttpException ex)
            {
                parseError = ex;
            }
            catch (Exception)
            {
                parseError = new HttpInternalServerErrorException();
            }
            // Do we need to send back an error response ?
            if (parseError != null)
            {
                // TODO: Clear any content that might already be added
                response.ResponseCode = parseError.ResponseCode;
                response.ResponseMessage = parseError.Message;
            }
            // Apply the after filters here
            m_server.ApplyAfterFilters(request, response, context);

            if (decodedData != null)
            {
                var files = decodedData.Files.GetFilesTempPath();
                m_server.MultiPartFileStorageHandler.DeleteFiles(files);
            }
            
            // Write the response
            response.Send(output);
            output.Flush();
        }

        #region Internal Implementation
        /// <summary>
        /// Check for an upgrade to a web socket connection.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="response"></param>
        /// <returns></returns>
        private IWebSocketRequestHandler UpgradeToWebsocket(HttpRequest request, HttpResponse response)
        {
            // Check for required headers
            if (!(request.Headers.ContainsKey(HttpHeaders.Connection) && request.Headers[HttpHeaders.Connection].ToLower().Contains("upgrade")))
                return null;
            if (!(request.Headers.ContainsKey(HttpHeaders.Upgrade) && request.Headers[HttpHeaders.Upgrade].ToLower().Contains("websocket")))
                return null;
            if (!request.Headers.ContainsKey(SecWebSocketVersion))
                return null;
            int version;
            if (!(int.TryParse(request.Headers[SecWebSocketVersion], out version) && (version == 13)))
                return null;
            if (!request.Headers.ContainsKey(SecWebSocketKey))
                return null;
            // Make sure we have a handler for the URI
            string partial;
            IWebSocketRequestHandler handler = m_server.GetHandlerForWebSocket(request.URI, out partial);
            if (handler == null)
                return null;
            // Do we support the protocols requested?
            string protocol = null;
            if (request.Headers.ContainsKey(SecWebSocketProtocol))
            {
                foreach (string proto in request.Headers[SecWebSocketProtocol].Split(WebSocketProtocolSeparator))
                {
                    if (handler.WillAcceptRequest(partial, proto.Trim()))
                    {
                        protocol = proto.Trim();
                        break;
                    }
                }
            }
            else if (handler.WillAcceptRequest(partial, ""))
                protocol = "";
            if (protocol == null)
                return null;
            // Finish the handshake
            byte[] security = Encoding.UTF8.GetBytes(request.Headers[SecWebSocketKey].Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11");
            SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider();
            sha1.Initialize();
            sha1.HashCore(security, 0, security.Length);
            security = sha1.HashFinal();
            response.Headers[SecWebSocketAccept] = Convert.ToBase64String(security);
            response.Headers[HttpHeaders.Upgrade] = "websocket";
            response.Headers[HttpHeaders.Connection] = "Upgrade";
            response.ResponseCode = HttpResponseCode.SwitchingProtocols;
            if (protocol.Length > 0)
                response.Headers[SecWebSocketProtocol] = protocol;
            // And we are done
            return handler;
        }

        /// <summary>
        /// Parse the request and headers.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private HttpRequest ParseRequest(Stream input)
        {       
            _inputStream = input;
            // Parse the request first
            RequestParseState state = RequestParseState.StartLine;
            string line = "";
            HttpRequest request = null;
            var isContinueReading = true;

            try
            {
                while (m_connected && isContinueReading)
                {
                    // Keep trying to read a line
                    if (state != RequestParseState.Body)    
                        line = ReadLine();
                    
                    switch (state)
                    {
                        case RequestParseState.StartLine:
                            request = ParseRequestLine(line);
                            if (request == null)
                                return null; // Just let the connection close
                            state++;
                            break;
                        case RequestParseState.Headers:
                            if (line.Length == 0)
                                state++;
                            else
                                ParseHeaderLine(request, line);
                            break;
                        case RequestParseState.Body:

                            isContinueReading = request.Headers.ContainsKey(HttpHeaders.ContentType);

                            if (isContinueReading)
                            {
                                var contentTypeValue = request.Headers[HttpHeaders.ContentType];

                                if (!request.Headers.ContainsKey(HttpHeaders.ContentLength))
                                    throw new HttpLengthRequiredException();

                                if (!int.TryParse(request.Headers[HttpHeaders.ContentLength], out _contentLength))
                                    throw new HttpLengthRequiredException();
                                
                                if (!string.IsNullOrEmpty(contentTypeValue))
                                {
                                    _tempDataIndexBeforeBodyReading = _tempDataCurrentIndex;

                                    decodedData = new DecodedData();

                                    if (contentTypeValue.Contains("application/x-www-form-urlencoded"))
                                    {
                                        DecodeUrlData();
                                    }
                                    else if (contentTypeValue.Contains("multipart/form-data"))
                                    {
                                        isContinueReading = ExtractBoundaryDelimeter(request);

                                        if (isContinueReading)
                                            DecodeMultiPartData();
                                    }
                                }
                                else
                                    isContinueReading = false;
                            }
                            
                            break;
                    }

                    if (_endOfStream)
                        break;
                }
            }
            catch (HttpException ex)
            {
                throw ex;
            }
            catch (Exception)
            {
                throw new HttpInternalServerErrorException("Error parsing request.");
            }
            // All done
            return request;
        }

        /// <summary>
        /// Parse a single header line
        /// </summary>
        /// <param name="request"></param>
        /// <param name="line"></param>
        private void ParseHeaderLine(HttpRequest request, string line)
        {
            if (line.StartsWith(" "))
            {
                // Continuation
                if (m_lastHeader == null)
                    throw new HttpBadRequestException("Invalid header format.");
                request.Headers[m_lastHeader] = request.Headers[m_lastHeader] + line;
            }
            else
            {
                Match match = HeaderLine.Match(line);
                if (match.Groups.Count != 3)
                    throw new HttpBadRequestException("Cannot parse header.");
                m_lastHeader = match.Groups[1].Value.Trim();
                request.Headers[m_lastHeader] = match.Groups[2].Value.Trim();
            }
        }

        private bool ExtractBoundaryDelimeter(HttpRequest request)
        {
            if (request != null && request.Headers.ContainsKey(HttpHeaders.ContentType))
            {
                var contentTypeValue = request.Headers[HttpHeaders.ContentType];

                if (!string.IsNullOrEmpty(contentTypeValue) && contentTypeValue.Contains("multipart/form-data"))
                {
                    var index = contentTypeValue.IndexOf("boundary");

                    if (index > 0)
                    {
                        var delimeterContent = contentTypeValue.Substring(index, contentTypeValue.Length - index);
                        var delimeterKeyValue = delimeterContent.Split('=');
                        _boundaryDelimeter = delimeterKeyValue[1];
                    }
                }
            }

            return !string.IsNullOrEmpty(_boundaryDelimeter);
        }

        private void DecodeUrlData()
        {
            try
            {
                //var data = Encoding.UTF8.GetString(_tempData, _tempDataCurrentIndex, _dataRead - _tempDataCurrentIndex);

                StringBuilder sb = new StringBuilder();
                sb.Length = 0;

                while (!_endOfStream)
                {
                    sb.Append(ReadLine());
                }

                var keyValuePairArray = sb.ToString().Split('&');
                foreach (var kv in keyValuePairArray)
                {
                    var keyValuePair = kv.Split('=');
                    var key = WebUtility.UrlDecode(keyValuePair[0]);
                    var value = WebUtility.UrlDecode(keyValuePair[1]);
                    decodedData.Parameters.Add(key, value);   
                }
            }
            catch (ArgumentException err)
            {
                throw new FormatException(err.Message, err);
            }
        }

        private void DecodeMultiPartData()
        {
            if (_tempDataCurrentIndex == _dataRead)
                FillBuffer(-1);

            if (_endOfStream)
                return;
            
            Element element;
            while ((element = ReadNextElement()) != null)
            {
                if (string.IsNullOrEmpty(element.Name))
                    throw new FormatException("Error parsing request. Missing value name.\nElement: " + element);

                if (!string.IsNullOrEmpty(element.Filename))
                {
                    if (string.IsNullOrEmpty(element.ContentType))
                        throw new FormatException("Error parsing request. Value '" + element.Name +
                                                    "' lacks a content type.");
                        
                    var file = new HttpFile
                    {
                        Name = element.Name,
                        OriginalFileName = element.Filename,
                        ContentType = element.ContentType,
                        TempFileName = element.TempFilePath
                    };
                    decodedData.Files.Add(file);
                }
                else
                {
                    decodedData.Parameters.Add(WebUtility.UrlDecode(element.Name), element.Content);
                }

                if (_endOfStream)
                    break;
            }
        }

        private void UpdateConentBody(HttpRequest request)
        {
            //23.08.2016 - Changes for supporting POST Method 
            MemoryStream content = new MemoryStream();
            
            while (m_connected && !_endOfStream)
            {
                content.Write(_tempData, _tempDataCurrentIndex, _dataRead);

                FillBuffer(-1);
            }
            //23.08.2016 - End of Changes for supporting POST Method

            // Did the connection drop while reading?
            if (!m_connected)
                return;
            // Reset the stream location and attach it to the request
            content.Seek(0, SeekOrigin.Begin);
            request.Content = content;
        }

        public Element ReadNextElement()
        {
            if (ReadBoundary()) //End of boundary code
            {
                _endOfStream = true;
                return null;
            }

            _currentElement = new Element();
            string header;
            while ((header = ReadHeaders()) != null)
            {
                if (StrUtils.StartsWith(header, "Content-Disposition:", true))
                {
                    _currentElement.Name = GetContentDispositionAttribute(header, "name");
                    _currentElement.Filename = StripPath(GetContentDispositionAttributeWithEncoding(header, "filename"));
                }
                else if (StrUtils.StartsWith(header, "Content-Type:", true))
                {
                    _currentElement.ContentType = header.Substring("Content-Type:".Length).Trim();
                }
            }

            MoveToNextBoundary();

            return _currentElement;
        }

        private bool ReadBoundary()
        {
            string line = ReadLine();
            while (line == "")
                line = ReadLine();
            if (line[0] != '-' || line[1] != '-')
                return false;

            if (!StrUtils.EndsWith(line, _boundaryDelimeter, false))
                return true;
            
            return false;
        }

        private string ReadHeaders()
        {
            string s = ReadLine();
            if (s == "")
                return null;

            return s;
        }
        
        private void FillBuffer(int bufferOffset)
        {
            try
            {
                if (_tempDataIndexBeforeBodyReading != -1 && _dataRead - _tempDataIndexBeforeBodyReading == _contentLength)
                {
                    _endOfStream = true;
                    return;
                }
                
                if (bufferOffset < 0)
                {
                    bufferOffset = 0;
                }

                _tempData = new byte[10000000];
                _dataRead = _inputStream.Read(_tempData, bufferOffset, _tempData.Length);
                
                if (_dataRead == 0)
                {
                    _endOfStream = true;
                    m_connected = false;
                }

                _dataRead += bufferOffset;
                _tempDataCurrentIndex = 0;
            }
            catch (Exception e)
            {
            }
            
        }

        private void MoveToNextBoundary()
        {
            var bufferOffset = 0;

            while (true)
            {
                if (_tempData == null || _dataRead == _tempDataCurrentIndex)
                {
                    FillBuffer(bufferOffset);
                    if (_endOfStream)
                        return;
                        
                    bufferOffset = 0;
                }

                _startIndex = _tempDataCurrentIndex;

                var boundaryFind = false;

                while (_tempDataCurrentIndex < _dataRead)
                {
                    int c = _tempData[_tempDataCurrentIndex++];

                    if (c == CR)
                    {
                        if (_tempDataCurrentIndex == _dataRead)
                        {
                            bufferOffset = 1;
                            break;
                        }

                        c = _tempData[_tempDataCurrentIndex++];

                        if (c != LF)
                            continue;

                        if (_tempDataCurrentIndex == _dataRead)
                        {
                            bufferOffset = 2;
                            break;
                        }

                        c = _tempData[_tempDataCurrentIndex++];

                        if (c != '-')
                            continue;

                        if (_tempDataCurrentIndex == _dataRead)
                        {
                            bufferOffset = 3;
                            break;
                        }

                        c = _tempData[_tempDataCurrentIndex++];

                        if (c == '-')
                        {
                            boundaryFind = true;
                            break;
                        }
                    }
                }

                int noOfBytes = 0;

                if (boundaryFind || _tempDataCurrentIndex == _dataRead)
                {
                    if (boundaryFind)
                    {
                        noOfBytes = _tempDataCurrentIndex - 4 - _startIndex;
                        _tempDataCurrentIndex -= 2;
                    }
                    else
                    {
                        noOfBytes = _tempDataCurrentIndex - bufferOffset - _startIndex;

                        if (bufferOffset == 1)
                            _tempData[0] = _tempData[_tempDataCurrentIndex - 3];

                        if (bufferOffset == 2)
                            _tempData[1] = _tempData[_tempDataCurrentIndex - 2];

                        if (bufferOffset == 3)
                            _tempData[2] = _tempData[_tempDataCurrentIndex - 1];
                    }
                }

                if (_currentElement.Filename != null)
                {
                    if (_currentElement.Filename != "")
                    {
                        if (string.IsNullOrEmpty(_currentElement.TempFilePath))
                            _currentElement.TempFilePath = m_server.MultiPartFileStorageHandler.GetTempFilePath(_currentElement.Filename);

                        var stream = m_server.MultiPartFileStorageHandler.GetFile(Path.GetFileName(_currentElement.TempFilePath));
                        stream.Write(_tempData, _startIndex, noOfBytes);
                        stream.Flush();
                        stream.Dispose();
                    }
                }
                else
                {
                    _currentElement.Content = Encoding.UTF8.GetString(_tempData, _startIndex, noOfBytes);
                }

                if (boundaryFind)
                    return;
            }
        }
        
        private string ReadLine()
        {
            StringBuilder sb = new StringBuilder();
            // CRLF or LF are ok as line endings.

            bool got_cr = false;
            int b = 0;
            sb.Length = 0;

            try
            {
                while (true)
                {
                    if (_tempDataCurrentIndex == _dataRead)
                    {
                        FillBuffer(-1);
                        if (_endOfStream)
                        { 
                            break;
                        }
                    }

                    b = _tempData[_tempDataCurrentIndex++];

                    if (b == LF)
                    {
                        break;
                    }

                    got_cr = (b == CR);
                    sb.Append((char)b);
                }

                if (got_cr)
                    sb.Length--;
            }
            catch (Exception e)
            {
                
            }
            
            return sb.ToString();
        }
        
        private static string GetContentDispositionAttribute(string l, string name)
        {
            int idx = l.IndexOf(name + "=\"");
            if (idx < 0)
                return null;
            int begin = idx + name.Length + "=\"".Length;
            int end = l.IndexOf('"', begin);
            if (end < 0)
                return null;
            if (begin == end)
                return "";
            return l.Substring(begin, end - begin);
        }

        private string GetContentDispositionAttributeWithEncoding(string l, string name)
        {
            int idx = l.IndexOf(name + "=\"");
            if (idx < 0)
                return null;
            int begin = idx + name.Length + "=\"".Length;
            int end = l.IndexOf('"', begin);
            if (end < 0)
                return null;
            if (begin == end)
                return "";

            string temp = l.Substring(begin, end - begin);
            var source = new byte[temp.Length];
            for (int i = temp.Length - 1; i >= 0; i--)
                source[i] = (byte)temp[i];

            return Encoding.UTF8.GetString(source, 0, source.Length);
        }

        private static string StripPath(string path)
        {
            if (path == null || path.Length == 0)
                return path;

            if (path.IndexOf(":\\") != 1 && !path.StartsWith("\\\\"))
                return path;
            return path.Substring(path.LastIndexOf('\\') + 1);
        }

        /// <summary>
        /// Parse the request start line
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        private HttpRequest ParseRequestLine(string line)
        {
            Match match;
            lock (RequestStartLine)
                match = RequestStartLine.Match(line);
            if (match.Groups.Count != 4)
                return null;
            // Get the method used
            HttpMethod method;
            if (!Enum.TryParse<HttpMethod>(match.Groups[1].Value, true, out method))
                return null;
            HttpRequest request = new HttpRequest(method, match.Groups[2].Value);
            // TODO: Should really check the HTTP version here as well
            return request;
        }

        /// <summary>
        /// Extract bytes from the input buffer (with an optional copy)
        /// </summary>
        /// <param name="count"></param>
        /// <param name="copy"></param>
        /// <returns></returns>
        private int ExtractBytes(int count, byte[] copy = null)
        {
            // Trim the number of bytes to that available
            count = (count > m_index) ? m_index : count;
            // Make a copy if requested
            if (copy != null)
                Array.Copy(m_buffer, copy, count);
            // Shuffle everything down
            Array.Copy(m_buffer, count, m_buffer, 0, m_index - count);
            m_index -= count;
            return count;
        }

        /// <summary>
        /// Read data from the stream into the buffer.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private void ReadData(Stream input)
        {
            try
            {
                int read = input.Read(m_buffer, m_index, m_buffer.Length - m_index);
                m_index += read;
                if (read == 0)
                    m_connected = false;
            }
            catch (Exception)
            {
                // Any error causes the connection to close
                m_connected = false;
            }
        }

        /// <summary>
        /// Read a line (terminated by CR/LF) from the input stream
        /// </summary>
        /// <param name="input"></param>
        /// <param name="line"></param>
        /// <returns></returns>
        private bool ReadLine(Stream input, out string line)
        {
            line = null;
            // Look for CR/LF pair
            for (int i = 0; i < (m_index - 1); i++)
            {
                if ((m_buffer[i] == CR) && (m_buffer[i + 1] == LF))
                {
                    // Extract the string (without the CR/LF)
                    line = Encoding.UTF8.GetString(m_buffer, 0, i);
                    ExtractBytes(i + 2);
                    return true;
                }
            }
            // No line yet, read more data
            ReadData(input);
            return false;
        }
        
        /// <summary>
        /// 23.08.2016 - Changes for supporting POST Method - Added a new overload for reading the content data from the input stream
		/// Read data from the stream into the buffer. 
		/// </summary>
		/// <param name="input"></param>
        /// <param name="offset"></param>
		/// <returns></returns>
		private void ReadData(Stream input, int count)
        {
            try
            {
                int read = input.Read(m_buffer, 0, count);
                m_index += read;
                if (read == 0)
                    m_connected = false;
            }
            catch (Exception exp)
            {
                // Any error causes the connection to close
                m_connected = false;
            }
        }
        
        #endregion

        public class Element
        {
            public string ContentType;
            public string Filename;
            public string TempFilePath;
            public string Content;
            public long Length;
            public string Name;
            public long Start;

            public override string ToString()
            {
                return "ContentType " + ContentType + ", Name " + Name + ", Filename " + Filename + ", Start " +
                       Start.ToString() + ", Length " + Length.ToString();
            }
        }
    }
}
