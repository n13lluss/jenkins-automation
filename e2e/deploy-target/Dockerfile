# SSH deploy target: the pipeline copies the compose file here and runs docker compose against the host daemon.
# sshd runs as the deploy user on port 2222 with its own host key, so nothing in the container runs as root.
FROM alpine:3.22
RUN apk add --no-cache openssh-server docker-cli docker-cli-compose \
    && adduser -D -u 1000 -s /bin/sh deploy \
    && passwd -u deploy \
    && mkdir -p /opt/orders /home/deploy/.ssh /home/deploy/sshd \
    && ssh-keygen -q -t ed25519 -N '' -f /home/deploy/sshd/ssh_host_ed25519_key \
    && printf '%s\n' 'Port 2222' 'HostKey /home/deploy/sshd/ssh_host_ed25519_key' 'PidFile /home/deploy/sshd/sshd.pid' \
        'AuthorizedKeysFile /run/e2e/deploy_key.pub' 'StrictModes no' 'PasswordAuthentication no' \
        'KbdInteractiveAuthentication no' 'AllowUsers deploy' > /home/deploy/sshd/sshd_config \
    && chown -R deploy:deploy /opt/orders /home/deploy
COPY entrypoint.sh /entrypoint.sh
USER deploy
EXPOSE 2222
ENTRYPOINT ["/bin/sh", "/entrypoint.sh"]
