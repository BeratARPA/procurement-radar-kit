import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../data/connection_store.dart';
import '../data/providers.dart';
import '../strings.dart';
import '../system/system_providers.dart';

/// localhost, 127.0.0.1 and ::1 - the machine asking, whichever one it is.
bool _isLoopback(String host) {
  final String bare = host.toLowerCase().replaceAll(RegExp(r'^\[|\]$'), '');

  return bare == 'localhost' || bare == '127.0.0.1' || bare == '::1';
}

/// Whether a loopback address could reach the api from wherever this is running.
///
/// True on a desktop build, which runs on the collecting machine. True in a
/// browser that was itself opened on localhost, because then the two are the
/// same machine. False on a phone, and false in a browser opened from another
/// machine's address - in both, loopback is the device holding the screen.
bool get _loopbackCouldWork {
  if (kIsWeb) return _isLoopback(Uri.base.host);

  return defaultTargetPlatform != TargetPlatform.android &&
      defaultTargetPlatform != TargetPlatform.iOS;
}

/// The address and the token, in one place used by two screens.
///
/// The token gate and the settings screen ask for exactly the same two things;
/// written twice they would validate differently within a month.
class ConnectionForm extends ConsumerStatefulWidget {
  const ConnectionForm({required this.submitLabel, this.onSaved, super.key});

  final String submitLabel;
  final VoidCallback? onSaved;

  @override
  ConsumerState<ConnectionForm> createState() => _ConnectionFormState();
}

class _ConnectionFormState extends ConsumerState<ConnectionForm> {
  final GlobalKey<FormState> _form = GlobalKey<FormState>();

  late final TextEditingController _baseUrl =
      TextEditingController(text: ref.read(connectionProvider).baseUrl);
  late final TextEditingController _token =
      TextEditingController(text: ref.read(connectionProvider).token);

  bool _revealed = false;
  String? _note;

  @override
  void dispose() {
    _baseUrl.dispose();
    _token.dispose();
    super.dispose();
  }

  /// Fills the token in from the project's `.env`.
  ///
  /// The one thing the desktop build can do that the browser cannot. It is a
  /// button rather than something automatic: silently filling a credential field
  /// from a file is a surprise, and on a machine that is not the collector there
  /// is no file to read anyway.
  Future<void> _fromEnv() async {
    final String? token =
        await ref.read(systemControlProvider).readEnv('PR_API_TOKEN');

    if (!mounted) return;

    setState(() {
      if (token != null) _token.text = token;
      _note = token == null ? Strings.projectFolderMissing : Strings.tokenFromEnvFound;
    });
  }

  Future<void> _save() async {
    if (!(_form.currentState?.validate() ?? false)) return;

    await ref.read(connectionProvider.notifier).save(
          Connection(
            // A trailing slash turns every generated path into a double slash,
            // which most servers forgive and some do not.
            baseUrl: _baseUrl.text.trim().replaceAll(RegExp(r'/+$'), ''),
            token: _token.text.trim(),
          ),
        );

    if (!mounted) return;

    ScaffoldMessenger.of(context)
        .showSnackBar(const SnackBar(content: Text(Strings.saved)));

    widget.onSaved?.call();
  }

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    return Form(
      key: _form,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: <Widget>[
          TextFormField(
            controller: _baseUrl,
            keyboardType: TextInputType.url,
            autocorrect: false,
            decoration: const InputDecoration(
              labelText: Strings.apiAddress,
              helperText: Strings.apiAddressHelp,
              helperMaxLines: 3,
              border: OutlineInputBorder(),
            ),
            validator: (String? value) {
              final String text = (value ?? '').trim();

              if (text.isEmpty) return Strings.addressEmpty;

              final Uri? uri = Uri.tryParse(text);

              if (uri == null || !uri.hasScheme || uri.host.isEmpty) {
                return Strings.addressNotAbsolute;
              }

              // The default is localhost, which is right on the machine doing
              // the collecting and cannot work anywhere else - on a phone it
              // means the phone. The help text under this field said so and it
              // was not enough: the field arrives already filled in, so there is
              // nothing to read before it looks correct. Refused rather than
              // warned, because there is no case where it would have worked.
              if (_isLoopback(uri.host) && !_loopbackCouldWork) {
                return Strings.addressLoopbackElsewhere;
              }

              return null;
            },
          ),
          const SizedBox(height: 20),
          TextFormField(
            controller: _token,
            obscureText: !_revealed,
            autocorrect: false,
            enableSuggestions: false,
            decoration: InputDecoration(
              labelText: Strings.apiToken,
              helperText: Strings.apiTokenHelp,
              helperMaxLines: 3,
              border: const OutlineInputBorder(),
              suffixIcon: IconButton(
                onPressed: () => setState(() => _revealed = !_revealed),
                icon: Icon(
                  _revealed ? Icons.visibility_off_outlined : Icons.visibility_outlined,
                ),
              ),
            ),
            validator: (String? value) =>
                (value ?? '').trim().isEmpty ? Strings.tokenEmpty : null,
            onFieldSubmitted: (_) => _save(),
          ),
          if (ref.watch(systemSupportedProvider)) ...<Widget>[
            const SizedBox(height: 8),
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                onPressed: _fromEnv,
                icon: const Icon(Icons.folder_open_outlined, size: 18),
                label: const Text(Strings.readFromEnv),
              ),
            ),
            if (_note != null)
              Text(
                _note!,
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.outline),
              ),
          ],
          const SizedBox(height: 12),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Icon(Icons.info_outline, size: 16, color: theme.colorScheme.outline),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  Strings.apiTokenStorageWarning,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: theme.colorScheme.outline),
                ),
              ),
            ],
          ),
          const SizedBox(height: 24),
          FilledButton(onPressed: _save, child: Text(widget.submitLabel)),
        ],
      ),
    );
  }
}
