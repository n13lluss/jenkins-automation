package com.example.orders;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayDeque;
import java.util.Deque;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import org.springframework.security.core.AuthenticationException;
import org.springframework.security.web.authentication.SimpleUrlAuthenticationFailureHandler;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

/** Answers 429 to a login for a username with MAX_FAILURES failed logins in the last WINDOW. */
@Component
public class LoginAttempts extends SimpleUrlAuthenticationFailureHandler {
    static final int MAX_FAILURES = 5;
    static final Duration WINDOW = Duration.ofMinutes(15);

    private final Map<String, Deque<Instant>> failures = new ConcurrentHashMap<>();
    private final Clock clock = Clock.systemUTC();

    public LoginAttempts() {
        super("/login?error");
    }

    @Override
    public void onAuthenticationFailure(HttpServletRequest request, HttpServletResponse response, AuthenticationException e)
            throws IOException, ServletException {
        Deque<Instant> list = failures.computeIfAbsent(key(request), k -> new ArrayDeque<>());
        synchronized (list) {
            list.addLast(clock.instant());
        }
        super.onAuthenticationFailure(request, response, e);
    }

    void reset(HttpServletRequest request) {
        failures.remove(key(request));
    }

    boolean blocked(HttpServletRequest request) {
        Deque<Instant> list = failures.get(key(request));
        if (list == null) {
            return false;
        }
        synchronized (list) {
            Instant cutoff = clock.instant().minus(WINDOW);
            while (!list.isEmpty() && list.peekFirst().isBefore(cutoff)) {
                list.pollFirst();
            }
            return list.size() >= MAX_FAILURES;
        }
    }

    private static String key(HttpServletRequest request) {
        String user = request.getParameter("username");
        return user == null ? "" : user.trim().toLowerCase(Locale.ROOT);
    }

    /** Runs before the login filter so a blocked username is refused without checking the password. */
    public OncePerRequestFilter filter() {
        return new OncePerRequestFilter() {
            @Override
            protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
                    throws ServletException, IOException {
                if ("POST".equals(request.getMethod()) && "/login".equals(request.getRequestURI().substring(request.getContextPath().length())) && blocked(request)) {
                    response.setStatus(429);
                    response.setHeader("Retry-After", String.valueOf(WINDOW.toSeconds()));
                    response.setContentType("text/plain");
                    response.getWriter().write("Too many failed logins");
                    return;
                }
                chain.doFilter(request, response);
            }
        };
    }
}
