# Jenkins used only to lint generated Jenkinsfiles (compose profile "validation").
FROM jenkins/jenkins:2.528.1-lts-jdk21
ENV JAVA_OPTS="-Djenkins.install.runSetupWizard=false" CASC_JENKINS_CONFIG=/var/jenkins_home/casc.yaml
RUN jenkins-plugin-cli --plugins workflow-aggregator pipeline-model-definition pipeline-utility-steps configuration-as-code
COPY casc.yaml /var/jenkins_home/casc.yaml
USER jenkins
