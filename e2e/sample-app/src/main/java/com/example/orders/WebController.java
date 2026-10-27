package com.example.orders;

import java.security.Principal;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
public class WebController {
    @GetMapping("/")
    public String home(Principal principal) {
        return "Orders for " + principal.getName();
    }

    @GetMapping("/admin")
    public String admin() {
        return "Admin area";
    }
}
