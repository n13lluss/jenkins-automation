# Jenkins controller for the PipelineForge end-to-end test: plugins the generated pipeline needs,
# configured as code, with the Docker CLI for docker agents (the host daemon is mounted).
FROM docker:28.5.1-cli AS docker-cli

FROM jenkins/jenkins:2.580.1-lts-jdk21
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
# The test repository and workspaces are owned by other uids.
USER root
RUN git config --system safe.directory '*'
USER jenkins
COPY plugins.txt /usr/share/jenkins/ref/plugins.txt
RUN jenkins-plugin-cli --plugin-file /usr/share/jenkins/ref/plugins.txt
COPY casc.yaml /usr/share/jenkins/ref/casc.yaml
ENV CASC_JENKINS_CONFIG=/usr/share/jenkins/ref/casc.yaml \
    JAVA_OPTS="-Djenkins.install.runSetupWizard=false -Dhudson.plugins.git.GitSCM.ALLOW_LOCAL_CHECKOUT=true"
