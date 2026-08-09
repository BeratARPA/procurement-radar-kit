import 'package:shared_preferences/shared_preferences.dart';

/// Where the api is and what it wants in the header.
class Connection {
  const Connection({required this.baseUrl, required this.token});

  final String baseUrl;
  final String token;

  bool get isComplete => baseUrl.isNotEmpty && token.isNotEmpty;

  Connection copyWith({String? baseUrl, String? token}) => Connection(
        baseUrl: baseUrl ?? this.baseUrl,
        token: token ?? this.token,
      );
}

/// Reads and writes the two values the app needs before it can ask anything.
///
/// `SharedPreferencesAsync`, not `SharedPreferences.getInstance()`: the latter
/// is documented as the one that will be deprecated, and on the web the two do
/// not share a key namespace, so a value written through one is invisible to the
/// other. Choosing here costs nothing; choosing later costs a migration.
///
/// **What this does not do is protect the token.** On the web this is
/// `window.localStorage`, in the clear, two clicks away in the browser's
/// developer tools. `flutter_secure_storage` was considered and rejected for two
/// reasons: on the web it encrypts the value and then writes the key beside it
/// in the same localStorage, which is obfuscation and not a boundary; and it
/// needs `crypto.subtle`, which browsers expose only in a secure context, so it
/// would work on `http://localhost` here and fail on `http://<machine-ip>` from
/// a phone - which is the way D-16 says this app is actually opened.
///
/// The token is a header on plain http across a home network on every request.
/// Encrypting it at rest while broadcasting it on the wire would be theatre. The
/// settings screen says this in the open rather than implying a safety that is
/// not there. Revisit when D-16's Tailscale or Cloudflare Tunnel lands, or when
/// the api gets TLS.
class ConnectionStore {
  ConnectionStore([SharedPreferencesAsync? preferences])
      : _preferences = preferences ?? SharedPreferencesAsync();

  static const String _baseUrlKey = 'api_base_url';
  static const String _tokenKey = 'api_token';

  /// The machine the worker runs on, seen from itself. Right for the desktop
  /// build and for a browser on the same machine; wrong from a phone, which is
  /// why the settings screen shows this field rather than hiding it.
  static const String defaultBaseUrl = 'http://localhost:5080';

  final SharedPreferencesAsync _preferences;

  Future<Connection> read() async {
    final String? baseUrl = await _preferences.getString(_baseUrlKey);
    final String? token = await _preferences.getString(_tokenKey);

    return Connection(baseUrl: baseUrl ?? defaultBaseUrl, token: token ?? '');
  }

  Future<void> write(Connection connection) async {
    await _preferences.setString(_baseUrlKey, connection.baseUrl);
    await _preferences.setString(_tokenKey, connection.token);
  }
}
