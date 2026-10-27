#!/bin/sh
# Grants the deploy user the Docker socket's group, installs its key and app.env, then runs sshd.
set -eu
gid=$(stat -c %g /var/run/docker.sock)
getent group "$gid" >/dev/null || addgroup -g "$gid" dockerhost
addgroup deploy "$(getent group "$gid" | cut -d: -f1)"
install -o deploy -g deploy -m 600 /run/e2e/deploy_key.pub /home/deploy/.ssh/authorized_keys
install -o deploy -g deploy -m 600 /run/e2e/app.env /opt/orders/app.env
exec /usr/sbin/sshd -D -e
