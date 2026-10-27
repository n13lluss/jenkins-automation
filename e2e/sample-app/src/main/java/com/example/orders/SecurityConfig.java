package com.example.orders;

import java.util.UUID;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.core.userdetails.User;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.security.crypto.factory.PasswordEncoderFactories;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.security.provisioning.InMemoryUserDetailsManager;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.SavedRequestAwareAuthenticationSuccessHandler;
import org.springframework.security.web.authentication.UsernamePasswordAuthenticationFilter;

@Configuration
public class SecurityConfig {

    @Bean
    SecurityFilterChain security(HttpSecurity http, LoginAttempts attempts) throws Exception {
        SavedRequestAwareAuthenticationSuccessHandler success = new SavedRequestAwareAuthenticationSuccessHandler() {
            @Override
            public void onAuthenticationSuccess(jakarta.servlet.http.HttpServletRequest request,
                    jakarta.servlet.http.HttpServletResponse response,
                    org.springframework.security.core.Authentication authentication)
                    throws java.io.IOException, jakarta.servlet.ServletException {
                attempts.reset(request);
                super.onAuthenticationSuccess(request, response, authentication);
            }
        };
        http.authorizeHttpRequests(a -> a
                        .requestMatchers("/actuator/health/**", "/actuator/health", "/login", "/error").permitAll()
                        .requestMatchers("/admin/**", "/admin").hasRole("ADMIN")
                        .anyRequest().authenticated())
                .formLogin(f -> f.failureHandler(attempts).successHandler(success).permitAll())
                .logout(l -> l.logoutUrl("/logout").permitAll())
                // The pipeline's login checks post forms without a CSRF token; everything else keeps CSRF protection.
                .csrf(c -> c.ignoringRequestMatchers("/login", "/logout"))
                .addFilterBefore(attempts.filter(), UsernamePasswordAuthenticationFilter.class)
                .headers(h -> h
                        .contentSecurityPolicy(csp -> csp.policyDirectives("default-src 'self'; frame-ancestors 'none'; form-action 'self'"))
                        .permissionsPolicyHeader(p -> p.policy("camera=(), microphone=(), geolocation=()")));
        return http.build();
    }

    /** Test accounts for the access-control check; passwords come from the environment (random when unset). */
    @Bean
    UserDetailsService users(@Value("${E2E_USER_PASSWORD:}") String userPassword, @Value("${E2E_ADMIN_PASSWORD:}") String adminPassword) {
        PasswordEncoder encoder = PasswordEncoderFactories.createDelegatingPasswordEncoder();
        return new InMemoryUserDetailsManager(
                User.withUsername("user").password(encoder.encode(orRandom(userPassword))).roles("USER").build(),
                User.withUsername("admin").password(encoder.encode(orRandom(adminPassword))).roles("USER", "ADMIN").build());
    }

    private static String orRandom(String password) {
        return password == null || password.isBlank() ? UUID.randomUUID().toString() : password;
    }
}
