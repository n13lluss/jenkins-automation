#!/bin/sh
# Installs the app's env file for the compose project, then runs sshd as the deploy user.
# The Docker socket's group is added with group_add; the authorized key is read from the mounted secrets.
set -eu
install -m 600 /run/e2e/app.env /opt/orders/app.env
exec /usr/sbin/sshd -D -e -f /home/deploy/sshd/sshd_config
