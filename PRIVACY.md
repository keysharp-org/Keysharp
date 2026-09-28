# Keysharp Privacy Policy

Last updated: September 28, 2026

This policy covers Keysharp, including Keysharp Store Edition published by
Descolada, and the bundled Dash, Keyview editor, tools, and demos.

## Information collection and use

Keysharp is a programming language and desktop automation tool. It runs scripts
on your computer and does not require a Keysharp account. It does not include
advertising, usage analytics, or an automatic crash-report upload system. It does
not automatically send your scripts, documents, keystrokes, clipboard contents,
or screenshots to its developers.

Depending on the scripts and tools you run, Keysharp can access information such
as keyboard and mouse input, clipboard contents, window titles and text, screen
images, files, and system information. These features support the automation you
choose to run. For example, Window Spy displays window information, and the
Clipboard History demo keeps copied text in memory while it is running.

## Scripts and third-party code

Like other programming languages, Keysharp can run code that reads, changes,
saves, or transmits information. Scripts and packages run with the permissions
available to Keysharp; they are not isolated in a script security sandbox.

A script's data handling depends on its code and any services it uses. This
policy describes Keysharp's own behavior and does not determine the privacy
practices of third-party scripts, libraries, or applications created with it.
Review code and its source before running it, especially when it handles
sensitive information.

## Local storage and logs

Keysharp and its bundled tools store data locally where needed for their
features. This includes settings, the Keyview editor's autosaved scratchpad,
package caches, and files you or your scripts save. Error messages and script
output can contain script text, file paths, and other information being
processed. Keysharp does not automatically upload this output to its developers.

On Windows, settings and the Keyview scratchpad normally use
`%APPDATA%\Keysharp`. Package and embedded-browser data can also use
`%LOCALAPPDATA%\Keysharp` and package-manager storage locations. The Microsoft
Store edition may use Windows' redirected app-data locations.

Local files remain until you, the relevant tool, or the operating system removes
or replaces them. The Clipboard History demo provides a **Clear history** option;
its in-memory history also ends when the demo exits.

## Network connections and other services

Opening the Package Manager can contact package registries. Installing or
restoring packages can contact GitHub, NuGet, or other configured download
sources. Opening documentation or website links contacts the selected website.
Scripts can also make network requests or display web content.

These services receive information needed to handle requests, such as your IP
address, requested resource, and request headers. Scripts may send additional
information according to their code. Websites and embedded browsers may store
cookies or other browsing data; Keysharp does not use cookies for advertising or
analytics tracking.

Microsoft Store and Windows may separately process installation, usage, and
diagnostic information under Microsoft's privacy settings and policies. These
services, and any websites or package sources you use, have their own privacy
policies:

- [Microsoft Privacy Statement](https://www.microsoft.com/privacy/privacystatement)
- [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement)

## Your choices and security

You choose which scripts and tools to run and can stop them to end their ongoing
access. You can inspect, copy, or delete local files and settings, and use the
operating system's privacy, account, and network controls where available. Close
the relevant tool before deleting its saved data. Uninstalling Keysharp may leave
script files and other data saved outside the app's installation or managed
storage.

Local settings and editor files rely on your operating system's access controls;
Keysharp does not add encryption to those files. The security of data handled or
sent by a script depends on that script and the services it uses.

## Contact and support

For privacy questions, use the
[Keysharp issue tracker](https://github.com/keysharp-org/Keysharp/issues).
Issues are public: do not include passwords, private documents, or other
sensitive information. If you report a problem, the maintainers can see your
GitHub username and any details or attachments you choose to provide, and use
them to respond and maintain Keysharp. Public issue content may remain available
until edited or removed, subject to GitHub's controls and retention policies.

## Changes to this policy

Updates will be published on this page with a revised date.
