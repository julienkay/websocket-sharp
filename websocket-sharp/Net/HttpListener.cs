#region License
/*
 * HttpListener.cs
 *
 * This code is derived from HttpListener.cs (System.Net) of Mono
 * (http://www.mono-project.com).
 *
 * The MIT License
 *
 * Copyright (c) 2005 Novell, Inc. (http://www.novell.com)
 * Copyright (c) 2012-2016 sta.blockhead
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */
#endregion

#region Authors
/*
 * Authors:
 * - Gonzalo Paniagua Javier <gonzalo@novell.com>
 */
#endregion

#region Contributors
/*
 * Contributors:
 * - Liryna <liryna.stark@gmail.com>
 */
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Threading;

// TODO: Logging.
namespace WebSocketSharp.Net
{
  /// <summary>
  /// Provides a simple, programmatically controlled HTTP listener.
  /// </summary>
  public sealed class HttpListener : IDisposable
  {
    #region Private Fields

    private AuthenticationSchemes                            _authSchemes;
    private Func<HttpListenerRequest, AuthenticationSchemes> _authSchemeSelector;
    private string                                           _certFolderPath;
    private Queue<HttpListenerContext>                       _contextQueue;
    private LinkedList<HttpListenerContext>                  _contextRegistry;
    private object                                           _contextRegistrySync;
    private static readonly string                           _defaultRealm;
    private bool                                             _disposed;
    private bool                                             _ignoreWriteExceptions;
    private volatile bool                                    _listening;
    private Logger                                           _logger;
    private HttpListenerPrefixCollection                     _prefixes;
    private string                                           _realm;
    private bool                                             _reuseAddress;
    private ServerSslConfiguration                           _sslConfig;
    private Func<IIdentity, NetworkCredential>               _userCredFinder;
    private Queue<HttpListenerAsyncResult>                   _waitQueue;

    #endregion

    #region Static Constructor

    static HttpListener ()
    {
      _defaultRealm = "SECRET AREA";
    }

    #endregion

    #region Public Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpListener"/> class.
    /// </summary>
    public HttpListener ()
    {
      _authSchemes = AuthenticationSchemes.Anonymous;
      _contextQueue = new Queue<HttpListenerContext> ();

      _contextRegistry = new LinkedList<HttpListenerContext> ();
      _contextRegistrySync = ((ICollection) _contextRegistry).SyncRoot;

      _logger = new Logger ();
      _prefixes = new HttpListenerPrefixCollection (this);
      _waitQueue = new Queue<HttpListenerAsyncResult> ();
    }

    #endregion

    #region Internal Properties

    internal bool IsDisposed {
      get {
        return _disposed;
      }
    }

    internal bool ReuseAddress {
      get {
        return _reuseAddress;
      }

      set {
        _reuseAddress = value;
      }
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the scheme used to authenticate the clients.
    /// </summary>
    /// <value>
    /// One of the <see cref="WebSocketSharp.Net.AuthenticationSchemes"/> enum values,
    /// represents the scheme used to authenticate the clients. The default value is
    /// <see cref="WebSocketSharp.Net.AuthenticationSchemes.Anonymous"/>.
    /// </value>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public AuthenticationSchemes AuthenticationSchemes {
      get {
        CheckDisposed ();
        return _authSchemes;
      }

      set {
        CheckDisposed ();
        _authSchemes = value;
      }
    }

    /// <summary>
    /// Gets or sets the delegate called to select the scheme used to authenticate the clients.
    /// </summary>
    /// <remarks>
    /// If you set this property, the listener uses the authentication scheme selected by
    /// the delegate for each request. Or if you don't set, the listener uses the value of
    /// the <see cref="HttpListener.AuthenticationSchemes"/> property as the authentication
    /// scheme for all requests.
    /// </remarks>
    /// <value>
    /// A <c>Func&lt;<see cref="HttpListenerRequest"/>, <see cref="AuthenticationSchemes"/>&gt;</c>
    /// delegate that references the method used to select an authentication scheme. The default
    /// value is <see langword="null"/>.
    /// </value>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public Func<HttpListenerRequest, AuthenticationSchemes> AuthenticationSchemeSelector {
      get {
        CheckDisposed ();
        return _authSchemeSelector;
      }

      set {
        CheckDisposed ();
        _authSchemeSelector = value;
      }
    }

    /// <summary>
    /// Gets or sets the path to the folder in which stores the certificate files used to
    /// authenticate the server on the secure connection.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This property represents the path to the folder in which stores the certificate files
    ///   associated with each port number of added URI prefixes. A set of the certificate files
    ///   is a pair of the <c>'port number'.cer</c> (DER) and <c>'port number'.key</c>
    ///   (DER, RSA Private Key).
    ///   </para>
    ///   <para>
    ///   If this property is <see langword="null"/> or empty, the result of
    ///   <c>System.Environment.GetFolderPath
    ///   (<see cref="Environment.SpecialFolder.ApplicationData"/>)</c> is used as the default path.
    ///   </para>
    /// </remarks>
    /// <value>
    /// A <see cref="string"/> that represents the path to the folder in which stores
    /// the certificate files. The default value is <see langword="null"/>.
    /// </value>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public string CertificateFolderPath {
      get {
        CheckDisposed ();
        return _certFolderPath;
      }

      set {
        CheckDisposed ();
        _certFolderPath = value;
      }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the listener returns exceptions that occur when
    /// sending the response to the client.
    /// </summary>
    /// <value>
    /// <c>true</c> if the listener shouldn't return those exceptions; otherwise, <c>false</c>.
    /// The default value is <c>false</c>.
    /// </value>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public bool IgnoreWriteExceptions {
      get {
        CheckDisposed ();
        return _ignoreWriteExceptions;
      }

      set {
        CheckDisposed ();
        _ignoreWriteExceptions = value;
      }
    }

    /// <summary>
    /// Gets a value indicating whether the listener has been started.
    /// </summary>
    /// <value>
    /// <c>true</c> if the listener has been started; otherwise, <c>false</c>.
    /// </value>
    public bool IsListening {
      get {
        return _listening;
      }
    }

    /// <summary>
    /// Gets a value indicating whether the listener can be used with the current operating system.
    /// </summary>
    /// <value>
    /// <c>true</c>.
    /// </value>
    public static bool IsSupported {
      get {
        return true;
      }
    }

    /// <summary>
    /// Gets the logging functions.
    /// </summary>
    /// <remarks>
    /// The default logging level is <see cref="LogLevel.Error"/>. If you would like to change it,
    /// you should set the <c>Log.Level</c> property to any of the <see cref="LogLevel"/> enum
    /// values.
    /// </remarks>
    /// <value>
    /// A <see cref="Logger"/> that provides the logging functions.
    /// </value>
    public Logger Log {
      get {
        return _logger;
      }
    }

    /// <summary>
    /// Gets the URI prefixes handled by the listener.
    /// </summary>
    /// <value>
    /// A <see cref="HttpListenerPrefixCollection"/> that contains the URI prefixes.
    /// </value>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public HttpListenerPrefixCollection Prefixes {
      get {
        CheckDisposed ();
        return _prefixes;
      }
    }

    /// <summary>
    /// Gets or sets the name of the realm associated with the listener.
    /// </summary>
    /// <remarks>
    /// If this property is <see langword="null"/> or empty, <c>"SECRET AREA"</c> will be used as
    /// the name of the realm.
    /// </remarks>
    /// <value>
    /// A <see cref="string"/> that represents the name of the realm. The default value is
    /// <see langword="null"/>.
    /// </value>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public string Realm {
      get {
        CheckDisposed ();
        return _realm;
      }

      set {
        CheckDisposed ();
        _realm = value;
      }
    }

    /// <summary>
    /// Gets or sets the SSL configuration used to authenticate the server and
    /// optionally the client for secure connection.
    /// </summary>
    /// <value>
    /// A <see cref="ServerSslConfiguration"/> that represents the configuration used to
    /// authenticate the server and optionally the client for secure connection.
    /// </value>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public ServerSslConfiguration SslConfiguration {
      get {
        CheckDisposed ();
        return _sslConfig ?? (_sslConfig = new ServerSslConfiguration ());
      }

      set {
        CheckDisposed ();
        _sslConfig = value;
      }
    }

    /// <summary>
    /// Gets or sets a value indicating whether, when NTLM authentication is used,
    /// the authentication information of first request is used to authenticate
    /// additional requests on the same connection.
    /// </summary>
    /// <remarks>
    /// This property isn't currently supported and always throws
    /// a <see cref="NotSupportedException"/>.
    /// </remarks>
    /// <value>
    /// <c>true</c> if the authentication information of first request is used;
    /// otherwise, <c>false</c>.
    /// </value>
    /// <exception cref="NotSupportedException">
    /// Any use of this property.
    /// </exception>
    public bool UnsafeConnectionNtlmAuthentication {
      get {
        throw new NotSupportedException ();
      }

      set {
        throw new NotSupportedException ();
      }
    }

    /// <summary>
    /// Gets or sets the delegate called to find the credentials for an identity used to
    /// authenticate a client.
    /// </summary>
    /// <value>
    /// A <c>Func&lt;<see cref="IIdentity"/>, <see cref="NetworkCredential"/>&gt;</c> delegate
    /// that references the method used to find the credentials. The default value is
    /// <see langword="null"/>.
    /// </value>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public Func<IIdentity, NetworkCredential> UserCredentialsFinder {
      get {
        CheckDisposed ();
        return _userCredFinder;
      }

      set {
        CheckDisposed ();
        _userCredFinder = value;
      }
    }

    #endregion

    #region Private Methods

    private void cleanupContextQueue (bool force)
    {
      if (_contextQueue.Count == 0)
        return;

      if (force) {
        _contextQueue.Clear ();

        return;
      }

      var ctxs = _contextQueue.ToArray ();

      _contextQueue.Clear ();

      foreach (var ctx in ctxs) {
        ctx.ErrorStatusCode = 503;
        ctx.SendError ();
      }
    }

    private void cleanupContextRegistry ()
    {
      var cnt = _contextRegistry.Count;

      if (cnt == 0)
        return;

      var ctxs = new HttpListenerContext[cnt];
      _contextRegistry.CopyTo (ctxs, 0);

      _contextRegistry.Clear ();

      foreach (var ctx in ctxs)
        ctx.Connection.Close (true);
    }

    private void cleanupWaitQueue (Exception exception)
    {
      if (_waitQueue.Count == 0)
        return;

      var aress = _waitQueue.ToArray ();

      _waitQueue.Clear ();

      foreach (var ares in aress)
        ares.Complete (exception);
    }

    private void close (bool force)
    {
      if (_listening) {
        _listening = false;

        cleanupContextQueue (force);
        cleanupContextRegistry ();

        var name = GetType ().ToString ();
        var ex = new ObjectDisposedException (name);
        cleanupWaitQueue (ex);

        EndPointManager.RemoveListener (this);
      }

      _disposed = true;
    }

    #endregion

    #region Internal Methods

    internal HttpListenerAsyncResult BeginGetContext (
      HttpListenerAsyncResult asyncResult
    )
    {
      lock (_contextRegistrySync) {
        if (!_listening)
          throw new HttpListenerException (995);

        if (_contextQueue.Count == 0) {
          _waitQueue.Enqueue (asyncResult);
        }
        else {
          var ctx = _contextQueue.Dequeue ();
          asyncResult.Complete (ctx, true);
        }

        return asyncResult;
      }
    }

    internal void CheckDisposed ()
    {
      if (_disposed)
        throw new ObjectDisposedException (GetType ().ToString ());
    }

    internal string GetRealm ()
    {
      var realm = _realm;

      return realm != null && realm.Length > 0 ? realm : _defaultRealm;
    }

    internal Func<IIdentity, NetworkCredential> GetUserCredentialsFinder ()
    {
      return _userCredFinder;
    }

    internal bool RegisterContext (HttpListenerContext context)
    {
      if (!_listening)
        return false;

      lock (_contextRegistrySync) {
        if (!_listening)
          return false;

        _contextRegistry.AddLast (context);

        if (_waitQueue.Count == 0) {
          _contextQueue.Enqueue (context);
        }
        else {
          var ares = _waitQueue.Dequeue ();
          ares.Complete (context);
        }

        return true;
      }
    }

    internal AuthenticationSchemes SelectAuthenticationScheme (
      HttpListenerRequest request
    )
    {
      var selector = _authSchemeSelector;

      if (selector == null)
        return _authSchemes;

      try {
        return selector (request);
      }
      catch {
        return AuthenticationSchemes.None;
      }
    }

    internal void UnregisterContext (HttpListenerContext context)
    {
      lock (_contextRegistrySync)
        _contextRegistry.Remove (context);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Shuts down the listener immediately.
    /// </summary>
    public void Abort ()
    {
      if (_disposed)
        return;

      lock (_contextRegistrySync) {
        if (_disposed)
          return;

        close (true);
      }
    }

    /// <summary>
    /// Begins getting an incoming request asynchronously.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This asynchronous operation must be completed by calling
    ///   the EndGetContext method.
    ///   </para>
    ///   <para>
    ///   Typically, the EndGetContext method is called by
    ///   <paramref name="callback"/>.
    ///   </para>
    /// </remarks>
    /// <returns>
    /// An <see cref="IAsyncResult"/> that represents the status of
    /// the asynchronous operation.
    /// </returns>
    /// <param name="callback">
    /// An <see cref="AsyncCallback"/> delegate that references the method to
    /// invoke when the asynchronous operation completes.
    /// </param>
    /// <param name="state">
    /// An <see cref="object"/> that represents a user defined object to
    /// pass to <paramref name="callback"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   This listener has no URI prefix on which listens.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   This listener has not been started or is currently stopped.
    ///   </para>
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public IAsyncResult BeginGetContext (AsyncCallback callback, Object state)
    {
      CheckDisposed ();

      if (_prefixes.Count == 0) {
        var msg = "The listener has no URI prefix on which listens.";

        throw new InvalidOperationException (msg);
      }

      if (!_listening) {
        var msg = "The listener has not been started.";

        throw new InvalidOperationException (msg);
      }

      return BeginGetContext (new HttpListenerAsyncResult (callback, state));
    }

    /// <summary>
    /// Shuts down the listener.
    /// </summary>
    public void Close ()
    {
      if (_disposed)
        return;

      lock (_contextRegistrySync) {
        if (_disposed)
          return;

        close (false);
      }
    }

    /// <summary>
    /// Ends an asynchronous operation to get an incoming request.
    /// </summary>
    /// <remarks>
    /// This method completes an asynchronous operation started by calling
    /// the BeginGetContext method.
    /// </remarks>
    /// <returns>
    /// A <see cref="HttpListenerContext"/> that represents a request.
    /// </returns>
    /// <param name="asyncResult">
    /// An <see cref="IAsyncResult"/> instance obtained by calling
    /// the BeginGetContext method.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="asyncResult"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="asyncResult"/> was not obtained by calling
    /// the BeginGetContext method.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This method was already called for <paramref name="asyncResult"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public HttpListenerContext EndGetContext (IAsyncResult asyncResult)
    {
      CheckDisposed ();

      if (asyncResult == null)
        throw new ArgumentNullException ("asyncResult");

      var ares = asyncResult as HttpListenerAsyncResult;

      if (ares == null) {
        var msg = "A wrong IAsyncResult instance.";

        throw new ArgumentException (msg, "asyncResult");
      }

      if (ares.EndCalled) {
        var msg = "This IAsyncResult instance cannot be reused.";

        throw new InvalidOperationException (msg);
      }

      ares.EndCalled = true;

      if (!ares.IsCompleted)
        ares.AsyncWaitHandle.WaitOne ();

      return ares.GetContext (); // This may throw an exception.
    }

    /// <summary>
    /// Gets an incoming request.
    /// </summary>
    /// <remarks>
    /// This method waits for an incoming request and returns when a request is
    /// received.
    /// </remarks>
    /// <returns>
    /// A <see cref="HttpListenerContext"/> that represents a request.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   This listener has no URI prefix on which listens.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   This listener has not been started or is currently stopped.
    ///   </para>
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public HttpListenerContext GetContext ()
    {
      CheckDisposed ();

      if (_prefixes.Count == 0) {
        var msg = "The listener has no URI prefix on which listens.";

        throw new InvalidOperationException (msg);
      }

      if (!_listening) {
        var msg = "The listener has not been started.";

        throw new InvalidOperationException (msg);
      }

      var ares = BeginGetContext (new HttpListenerAsyncResult (null, null));
      ares.InGet = true;

      return EndGetContext (ares);
    }

    /// <summary>
    /// Starts receiving incoming requests.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public void Start ()
    {
      CheckDisposed ();

      if (_listening)
        return;

      lock (_contextRegistrySync) {
        CheckDisposed ();

        if (_listening)
          return;

        EndPointManager.AddListener (this);

        _listening = true;
      }
    }

    /// <summary>
    /// Stops receiving incoming requests.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// This listener has been closed.
    /// </exception>
    public void Stop ()
    {
      CheckDisposed ();

      if (!_listening)
        return;

      lock (_contextRegistrySync) {
        if (!_listening)
          return;

        _listening = false;

        cleanupContextQueue (false);
        cleanupContextRegistry ();

        var msg = "The listener is stopped.";
        var ex = new HttpListenerException (995, msg);
        cleanupWaitQueue (ex);

        EndPointManager.RemoveListener (this);
      }
    }

    #endregion

    #region Explicit Interface Implementations

    /// <summary>
    /// Releases all resources used by the listener.
    /// </summary>
    void IDisposable.Dispose ()
    {
      if (_disposed)
        return;

      lock (_contextRegistrySync) {
        if (_disposed)
          return;

        close (true);
      }
    }

    #endregion
  }
}
