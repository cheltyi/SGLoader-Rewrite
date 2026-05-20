#pragma once

#include <QMainWindow>
#include <QString>

class QLineEdit;
class QLabel;
class QPushButton;
class QPlainTextEdit;
class QTableWidget;
class GitHubClient;

class MainWindow : public QMainWindow
{
    Q_OBJECT

public:
    explicit MainWindow(QWidget *parent = nullptr);

protected:
    void dragEnterEvent(QDragEnterEvent *event) override;
    void dragLeaveEvent(QDragLeaveEvent *event) override;
    void dropEvent(QDropEvent *event) override;

private:
    void buildUi();
    void addFile(const QString &path);
    void browseForFiles();
    void clearList();
    void removeSelectedRows();
    void submit(bool approved);
    void appendLog(const QString &message);
    void updateButtons();
    void setControlsBusy(bool busy);
    void setDropHighlight(bool on);
    void loadSettings();
    void saveSettings();
    int rowForHash(const QString &hash) const;

    QLineEdit *m_tokenEdit = nullptr;
    QLineEdit *m_repoEdit = nullptr;
    QLineEdit *m_branchEdit = nullptr;
    QLabel *m_dropLabel = nullptr;
    QTableWidget *m_table = nullptr;
    QPushButton *m_approveButton = nullptr;
    QPushButton *m_rejectButton = nullptr;
    QPushButton *m_clearButton = nullptr;
    QPlainTextEdit *m_log = nullptr;

    GitHubClient *m_github = nullptr;
    bool m_busy = false;
};
