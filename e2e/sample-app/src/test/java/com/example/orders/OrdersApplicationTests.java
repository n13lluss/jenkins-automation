package com.example.orders;

import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestBuilders.formLogin;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.user;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.web.servlet.MockMvc;

@SpringBootTest(properties = {"E2E_USER_PASSWORD=user-test-password", "E2E_ADMIN_PASSWORD=admin-test-password"})
@AutoConfigureMockMvc
class OrdersApplicationTests {

    @Autowired
    MockMvc mvc;

    @Test
    void healthIsPublic() throws Exception {
        mvc.perform(get("/actuator/health")).andExpect(status().isOk());
    }

    @Test
    void adminAreaNeedsTheAdminRole() throws Exception {
        mvc.perform(get("/admin").with(user("user").roles("USER"))).andExpect(status().isForbidden());
        mvc.perform(get("/admin").with(user("admin").roles("USER", "ADMIN"))).andExpect(status().isOk());
        mvc.perform(get("/admin")).andExpect(status().is3xxRedirection());
    }

    @Test
    void loginWorksWithTheConfiguredPassword() throws Exception {
        mvc.perform(formLogin().user("admin").password("admin-test-password")).andExpect(status().is3xxRedirection());
    }

    @Test
    void repeatedFailedLoginsAreThrottled() throws Exception {
        for (int i = 0; i < LoginAttempts.MAX_FAILURES; i++) {
            mvc.perform(post("/login").param("username", "probe").param("password", "wrong")).andExpect(status().is3xxRedirection());
        }
        mvc.perform(post("/login").param("username", "probe").param("password", "wrong")).andExpect(status().isTooManyRequests());
    }
}
