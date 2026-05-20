#include <QApplication>

#include "mainwindow.h"

int main(int argc, char *argv[])
{
    QApplication app(argc, argv);
    QApplication::setApplicationName("PatchValidator");
    QApplication::setOrganizationName("SGLoader");

    MainWindow window;
    window.show();

    return app.exec();
}
