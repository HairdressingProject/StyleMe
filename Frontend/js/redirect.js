import { authenticate, getUser, logout } from './authenticate';

async function redirect() {
    const logoutLink = document.getElementById('logout');
    const userName = document.getElementById('user-name');
    const id = await authenticate();

    if (id) {
        const { user } = await getUser(id) || { firstName: 'User' };
        userName.textContent = `${user.firstName} ${user.lastName || ""}`;
    }
    else {
        // not authenticated
        window.location.replace('/sign_in.php');
    }

    logoutLink.addEventListener('click', async function (e) {
        e.preventDefault();
        await logout();
    });
}

export { redirect }