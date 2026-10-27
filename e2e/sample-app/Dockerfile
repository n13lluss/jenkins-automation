# Sample app image for the PipelineForge end-to-end test. Pinned base, non-root user.
FROM eclipse-temurin:21.0.8_9-jre-noble
WORKDIR /app
COPY target/orders.jar /app/app.jar
USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["java", "-XX:MaxRAMPercentage=75", "-jar", "/app/app.jar"]
