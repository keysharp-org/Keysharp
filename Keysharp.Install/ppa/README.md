# Launchpad PPA

Keysharp, keysharp-input and keysharp-desktop are published to one Launchpad PPA,
named by `PPA` in each project's release workflow, so one `add-apt-repository`
makes all three available. Each workflow uploads a source package per Ubuntu series
in `PPA_SERIES`, and Launchpad builds the architectures enabled for the PPA. The
package definition is the one the GitHub `.deb` uses: `Keysharp.Install/linux/debian/`
here, `packaging/debian/` in the components.

## Release flow

1. `ppa-source` builds the unsigned source uploads (`build-source.sh`).
2. `ppa-rehearse` builds every series on amd64 and arm64 in a clean container,
   offline, unprivileged and without a `HOME`, as Launchpad does (`rehearse.sh`).
3. `publish-ppa` signs and uploads them once every rehearsal has passed (`upload.sh`).

Keysharp uploads when a release is published (`draft=false`); the components upload
on their tag. Launchpad accepts a version once, so a rerun skips what the PPA already
has, and the `ppa_revision` input uploads a released version again, reusing the
upstream tarball Launchpad holds.

## Keysharp's upstream tarball

Launchpad's builders have no network, so the tarball carries the Eto revision
`flake.lock` pins, the KPM release `Keysharp.Payload.proj` pins, and a NuGet feed
restored by each series' own `dotnet-sdk-10.0` (`restore-feed.sh`). The feed matches
that SDK's patch release: if Ubuntu updates dotnet before Launchpad builds, the next
Keysharp release is the fix.

## Signing

Uploads are signed with an OpenPGP key registered to the Launchpad account that owns
the PPA. The workflows read its armored secret key from the Actions secret
`PPA_GPG_PRIVATE_KEY`, and its passphrase, if it has one, from `PPA_GPG_PASSPHRASE`.
Setting an organization secret with `gh` needs the `admin:org` scope:

```sh
gpg --armor --export-secret-keys <fingerprint> | gh secret set PPA_GPG_PRIVATE_KEY \
  --org <organization> --visibility selected --repos Keysharp,keysharp-input,keysharp-desktop
```

## Running locally

On Ubuntu with `debhelper` and Docker, with Eto and KPM checked out beside this
repository (or named by `ETO_CHECKOUT` and `KPM_CHECKOUT`):

```sh
RESTORE_IN=docker bash Keysharp.Install/ppa/build-source.sh
bash Keysharp.Install/ppa/rehearse.sh dist/ppa/keysharp_*~noble1.dsc noble
```

`build-source.sh` packages the committed `HEAD`.
