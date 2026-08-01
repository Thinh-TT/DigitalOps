import {
  createBrowserRouter,
  type RouteObject,
} from "react-router";
import { AppShell } from "./AppShell";
import {
  AuthenticatedRoute,
  AuthStatusBoundary,
  BusinessAccessRoute,
  PublicOnlyRoute,
  RoleRoute,
  RootRedirect,
} from "./route-guards";
import {
  ChangePasswordPage,
  LoginPage,
} from "../pages/AuthPages";
import { FeaturePlaceholderPage } from "../pages/FeaturePlaceholderPage";
import {
  StaffCreatePage,
  StaffDetailPage,
  StaffListPage,
} from "../pages/StaffPages";
import {
  MemberCreatePage,
  MemberDetailPage,
  MemberListPage,
} from "../pages/MemberPages";
import { MemberImportPage } from "../pages/MemberImportPage";
import {
  DocumentTemplateCreatePage,
  DocumentTemplateDetailPage,
  DocumentTemplateListPage,
  DocumentTypeDetailPage,
  DocumentTypeListPage,
} from "../pages/DocumentCatalogPages";
import { ForbiddenPage, NotFoundPage } from "../pages/StatusPages";
import {
  IncomingDocumentCreatePage,
  IncomingDocumentDetailPage,
  IncomingDocumentListPage,
} from "../pages/IncomingDocumentPages";
import { ReminderPage } from "../pages/ReminderPage";

const commonRoutes: RouteObject[] = [
  { path: "incoming-documents", element: <IncomingDocumentListPage /> },
  { path: "incoming-documents/:id", element: <IncomingDocumentDetailPage /> },
  { path: "reminders", element: <ReminderPage /> },
  placeholder("outgoing-documents", "SCR-011", "Văn bản đi", "Danh sách văn bản đi."),
  placeholder(
    "outgoing-documents/:id",
    "SCR-012 / SCR-013",
    "Chi tiết văn bản đi",
    "Soạn thảo, AI draft, review và lịch sử.",
  ),
  placeholder("search", "SCR-016", "Tìm kiếm toàn văn", "Tra cứu văn bản và nội dung file."),
];

const administratorRoutes: RouteObject[] = [
  {
    path: "staff",
    element: <StaffListPage />,
  },
  {
    path: "staff/new",
    element: <StaffCreatePage />,
  },
  {
    path: "staff/:id",
    element: <StaffDetailPage />,
  },
  { path: "document-types", element: <DocumentTypeListPage /> },
  { path: "document-types/:id", element: <DocumentTypeDetailPage /> },
  { path: "document-templates", element: <DocumentTemplateListPage /> },
  { path: "document-templates/new", element: <DocumentTemplateCreatePage /> },
  { path: "document-templates/:id", element: <DocumentTemplateDetailPage /> },
];

const memberRoutes: RouteObject[] = [
  {
    path: "members",
    element: <MemberListPage />,
  },
  {
    path: "members/new",
    element: <MemberCreatePage />,
  },
  {
    path: "members/import",
    element: <MemberImportPage />,
  },
  {
    path: "members/:id",
    element: <MemberDetailPage />,
  },
];

export const appRoutes: RouteObject[] = [
  {
    element: <AuthStatusBoundary />,
    children: [
      {
        index: true,
        element: <RootRedirect />,
      },
      {
        element: <PublicOnlyRoute />,
        children: [
          {
            path: "login",
            element: <LoginPage />,
          },
        ],
      },
      {
        element: <AuthenticatedRoute />,
        children: [
          {
            path: "change-password",
            element: <ChangePasswordPage />,
          },
          {
            element: <BusinessAccessRoute />,
            children: [
              {
                element: <AppShell />,
                children: [
                  ...commonRoutes,
                  {
                    element: <RoleRoute allowedRoles={["Clerk"]} />,
                    children: [
                      {
                        path: "incoming-documents/new",
                        element: <IncomingDocumentCreatePage />,
                      },
                      placeholder(
                        "archive-queue",
                        "SCR-015",
                        "Phát hành / lưu trữ",
                        "Hàng chờ cấp số và lưu trữ.",
                      ),
                    ],
                  },
                  {
                    element: <RoleRoute allowedRoles={["Drafter"]} />,
                    children: [
                      placeholder(
                        "outgoing-documents/new",
                        "SCR-011",
                        "Tạo văn bản đi",
                        "Khởi tạo văn bản theo mẫu.",
                      ),
                    ],
                  },
                  {
                    element: <RoleRoute allowedRoles={["Leader"]} />,
                    children: [
                      placeholder(
                        "approval-queue",
                        "SCR-014",
                        "Hàng chờ duyệt",
                        "Duyệt hoặc trả lại văn bản.",
                      ),
                    ],
                  },
                  {
                    element: <RoleRoute allowedRoles={["Administrator"]} />,
                    children: administratorRoutes,
                  },
                  {
                    element: (
                      <RoleRoute allowedRoles={["Administrator", "Clerk"]} />
                    ),
                    children: memberRoutes,
                  },
                ],
              },
            ],
          },
        ],
      },
      {
        path: "forbidden",
        element: <ForbiddenPage />,
      },
      {
        path: "*",
        element: <NotFoundPage />,
      },
    ],
  },
];

export const router = createBrowserRouter(appRoutes);

function placeholder(
  path: string,
  screen: string,
  title: string,
  description: string,
): RouteObject {
  return {
    path,
    element: (
      <FeaturePlaceholderPage
        screen={screen}
        title={title}
        description={description}
      />
    ),
  };
}
