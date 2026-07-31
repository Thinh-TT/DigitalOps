import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App as AntDesignApp, ConfigProvider } from "antd";
import viVN from "antd/locale/vi_VN";
import { RouterProvider } from "react-router/dom";
import { router } from "./app/router";
import { AuthProvider } from "./shared/auth/AuthProvider";
import "./styles.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ConfigProvider locale={viVN}>
      <AntDesignApp>
        <AuthProvider>
          <RouterProvider router={router} />
        </AuthProvider>
      </AntDesignApp>
    </ConfigProvider>
  </StrictMode>,
);
