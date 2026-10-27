# SSH deploy target: the pipeline copies the compose file here and runs docker compose against the host daemon.
FROM alpine:3.22
RUN apk add --no-cache openssh-server docker-cli docker-cli-compose \
    && adduser -D -u 1000 -s /bin/sh deploy \
    && passwd -u deploy \
    && ssh-keygen -A \
    && mkdir -p /opt/orders /home/deploy/.ssh \
    && chown deploy:deploy /opt/orders /home/deploy/.ssh \
    && chmod 700 /home/deploy/.ssh \
    && printf 'PasswordAuthentication no\nPermitRootLogin no\nAllowUsers deploy\n' >> /etc/ssh/sshd_config
COPY entrypoint.sh /entrypoint.sh
EXPOSE 22
ENTRYPOINT ["/bin/sh", "/entrypoint.sh"]
