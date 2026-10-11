# Private credential references

The application now has a Windows Generic Credential reader and a protected
alias resolver. Store token acquisition and product discovery are implemented and
fixture verified; Marketplace providers remain pending. The first authorized live
Store check resolved the actual credential and acquired a token without displaying
its value. It returned zero apps/add-ons, which mismatches the operator's roughly
43 published apps and 86 add-ons. The application's Partner Center association
was investigated in [the diagnostic](verification/store-association-diagnostic.md).
The operator now confirms the replacement application's saved Manager(Windows)
role. Its different-address credential was subsequently accepted in the
replacement token request. Newer reviews now contain apps; account/publication
coverage and add-on collection HTTP 400 remain unresolved. See
[the replacement diagnostic](verification/store-replacement-diagnostic.md).
Report permissions remain unverified.

Keep the secret value in Windows Credential Manager. Generic credentials store
application-defined secret data; AnalyticsET expects the Password field entered in
Windows Credential Manager as Unicode text. The username can contain the Secret ID,
but AnalyticsET does not read or use it to authenticate. See Microsoft's
[credential structure](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentialw)
and [CredRead](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credreadw).

The default private mapping file is
`%USERPROFILE%/.analyticset/credentials/references.json`. Create it in a dedicated
directory owned by the operator, with protected permissions granting only the
operator and SYSTEM access. The resolver verifies directory and file permissions,
rejects repository/redirected locations, and refuses unprotected mappings.

This fictional example contains targets and aliases, never secret IDs or values:

```json
{
  "schemaVersion": 1,
  "references": {
    "store-reference": {
      "target": "AnalyticsET/example-store/publisher-one",
      "generation": "1"
    }
  }
}
```

A runtime profile's `credentialReference` is the dictionary key. Its target is the
exact private Generic Credential address; do not put that address or a secret value
in shared examples. A reference can be reused by separately scoped resource bindings
when the operator has deliberately configured the same application credential.

Increment the safe generation label if remapping or deliberately invalidating a
reference. The resolver also incorporates Windows Credential Manager's modification
version into a one-way generation key so credential rotation invalidates later token
and inventory cache bindings. Secret-bearing objects redact ordinary string and JSON
serialization; secret values are supplied only to the authentication boundary.

Offline profile commands do not invoke this resolver. Missing mappings and unavailable
logon-session credentials produce an actionable error without exposing targets or
credential contents. The current Windows token selects the credential set; running
under a different or restricted account can require using the operator's own terminal.
